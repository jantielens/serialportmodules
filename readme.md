# Serial Port Modules
This is a sample implementation to build an Azure IoT Edge module that can receive messages over a serial port, and forwards those message to the IoT Hub (or other modules). The module can also send messages over a serial port, triggered by a direct method.
The solution is written in C# and currently contains one module (SerialPortModule).

## Requirements
- Azure Iot Edge running on a device
- Host operating system can be Windows or Linux, 32 or 64 bit
- The code expects every message is sent on exactely one line via the serial port

## Remarks
- The module is tested on a Raspberry Pi (and it works :) ).
- The module currently depends on System.IO.Ports version=4.6.0-preview4.19164.7. This preview version allows the System.IO.Ports namespace to be used on Linux as well.