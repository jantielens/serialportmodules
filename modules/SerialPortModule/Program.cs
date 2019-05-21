namespace SerialPortModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using System.IO.Ports;
    using System.Linq;

    class Program
    {
        static int counter;
        static SerialPort serialPort;
        static ModuleClient ioTHubModuleClient;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine(@"
███████╗███████╗██████╗ ██╗ █████╗ ██╗         ██████╗  ██████╗ ██████╗ ████████╗
██╔════╝██╔════╝██╔══██╗██║██╔══██╗██║         ██╔══██╗██╔═══██╗██╔══██╗╚══██╔══╝
███████╗█████╗  ██████╔╝██║███████║██║         ██████╔╝██║   ██║██████╔╝   ██║   
╚════██║██╔══╝  ██╔══██╗██║██╔══██║██║         ██╔═══╝ ██║   ██║██╔══██╗   ██║   
███████║███████╗██║  ██║██║██║  ██║███████╗    ██║     ╚██████╔╝██║  ██║   ██║   
╚══════╝╚══════╝╚═╝  ╚═╝╚═╝╚═╝  ╚═╝╚══════╝    ╚═╝      ╚═════╝ ╚═╝  ╚═╝   ╚═╝   
                                                ______            _       _       
                                                |  ___ \          | |     | |      
                                                | | _ | | ___   _ | |_   _| | ____ 
                                                | || || |/ _ \ / || | | | | |/ _  )
                                                | || || | |_| ( (_| | |_| | ( (/ / 
                                                |_||_||_|\___/ \____|\____|_|\____)        
                                    https://github.com/jantielens/serialportmodules                                                     
            ");
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            // Initialize Serial Port
            string[] ports = SerialPort.GetPortNames();
            Console.WriteLine("The following serial ports were found:");             // Display each port name to the console.
            foreach (string port in ports)
            {
                Console.WriteLine($" - {port}");
            }
            Console.WriteLine("End of serial ports.");

            string serialPortName = "/dev/ttyACM0";
            // try to read configured port env. vars
            if (Environment.GetEnvironmentVariable("portname") != null)
            {
                // found in env variables, use it
                serialPortName = Environment.GetEnvironmentVariable("portname");
                Console.WriteLine($"Found 'portname' environment variable, using port '{serialPortName}'.");
            }
            else
            {
                Console.WriteLine($"Environment variable 'portname' not found, using default name '{serialPortName}'.");
            }


            Console.WriteLine("Opening port.");
            try
            {
                serialPort = new SerialPort(serialPortName, 9600);
                serialPort.Open();
                serialPort.ReadExisting(); // flush what's in there
                serialPort.DataReceived += new SerialDataReceivedEventHandler(SerialDataReceived);
                Console.WriteLine($"Port {serialPortName} opened.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while opening port: { ex.ToString()}");
                throw new ApplicationException($"Could not open serial port '{serialPortName}': {ex.ToString()}");
            }

            await ioTHubModuleClient.SetMethodHandlerAsync("sendserial", OnSendSerial, null);
        }

        public static void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            try
            {
                string indata = sp.ReadLine(); // wait for new line is received
                Console.WriteLine("Received serial data: " + indata);
                byte[] bytes = Encoding.UTF8.GetBytes(indata);
                Console.WriteLine("Sending to hub ...");
                ioTHubModuleClient.SendEventAsync("output1", new Message(bytes));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while receiving/sending serial data: {ex.ToString()}");
            }
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await moduleClient.SendEventAsync("output1", pipeMessage);
                Console.WriteLine("Received message sent");
            }
            return MessageResponse.Completed;
        }

        private static async Task<MethodResponse> OnSendSerial(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Direct Method {methodRequest.Name} invoked.");
            Newtonsoft.Json.Linq.JObject jsonObject = null;
            try
            {
                jsonObject = Newtonsoft.Json.Linq.JObject.Parse(methodRequest.DataAsJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception while de-serializing received message: " + ex.ToString());


            }
            if (jsonObject != null)
            {
                try
                {
                    var data = jsonObject.Value<string>("message");
                    Console.WriteLine($"Sending message: {data}");
                    serialPort.WriteLine(data);
                    return new MethodResponse(200);
                }
                catch (Exception msgEx)
                {
                    Console.WriteLine($"Exception while retreiving 'message' property from JSON: " + msgEx.ToString());
                    return new MethodResponse(500);
                }
            }
            else
            {
                return new MethodResponse(500);
            }
        }
    }
}
