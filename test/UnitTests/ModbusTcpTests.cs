﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AMWD.Modbus.Common;
using AMWD.Modbus.Common.Structures;
using AMWD.Modbus.Tcp.Client;
using AMWD.Modbus.Tcp.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
	[TestClass]
	public class ModbusTcpTests
	{
		#region Modbus Client

		#region Control

		[TestMethod]
		public async Task ClientConnectTest()
		{
			using var server = new MiniTestServer();
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port);
			await client.Connect();
			Assert.IsTrue(client.IsConnected, "Client shoud be connected");

			await client.Disconnect();
			Assert.IsFalse(client.IsConnected, "Client should not be connected");
		}

		[TestMethod]
		public async Task ClientReconnectTest()
		{
			using var server = new MiniTestServer();
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected, "Client should be connected");

			await server.Stop();
			await EnsureWait();  // time to start reconnect
			Assert.IsFalse(client.IsConnected, "Client should not be connected");

			server.Start();
			await client.ConnectingTask;
			Assert.IsTrue(client.IsConnected, "Client should be connected again");
		}

		[TestMethod]
		public async Task ClientEventsTest()
		{
			int connectEvents = 0;
			int disconnectEvents = 0;
			using var server = new MiniTestServer();
			server.Start();

			using (var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger()))
			{
				client.Connected += (sender, args) =>
				{
					connectEvents++;
				};
				client.Disconnected += (sender, args) =>
				{
					disconnectEvents++;
				};

				Assert.AreEqual(0, connectEvents, "No connet events");
				Assert.AreEqual(0, disconnectEvents, "No disconnect events");

				await client.Connect();
				Assert.IsTrue(client.IsConnected, "Client should be connected");

				await EnsureWait(); // get events raised
				Assert.AreEqual(1, connectEvents, "One connect event");
				Assert.AreEqual(0, disconnectEvents, "No disconnect events");

				await server.Stop();

				await EnsureWait();  // time to set all information
				Assert.IsFalse(client.IsConnected, "Client should not be connected");

				await EnsureWait(); // get events raised
				Assert.AreEqual(1, connectEvents, "One connect event");
				Assert.AreEqual(1, disconnectEvents, "One disconnect event");

				server.Start();
				await client.ConnectingTask;
				Assert.IsTrue(client.IsConnected, "Client should be connected");

				await EnsureWait(); // get events raised
				Assert.AreEqual(2, connectEvents, "Two connect events");
			}

			await EnsureWait(); // get events raised
			Assert.AreEqual(2, disconnectEvents, "Two disconnect events");
		}

		#endregion Control

		#region Read

		[TestMethod]
		public async Task ClientReadExceptionTest()
		{
			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 2, 1, 0, 24, 0, 2 };
			string expectedExceptionMessage = ErrorCode.GatewayTargetDevice.GetDescription();

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending error response");
					return new byte[] { request[0], request[1], 0, 0, 0, 3, 2, 129, 11 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			try
			{
				var response = await client.ReadCoils(2, 24, 2);
				Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
				Assert.Fail("Exception not thrown");
			}
			catch (ModbusException ex)
			{
				Assert.AreEqual(expectedExceptionMessage, ex.Message);
			}
		}

		[TestMethod]
		public async Task ClientReadCoilsTest()
		{
			// Function Code 0x01

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 12, 1, 0, 20, 0, 10 };
			var expectedResponse = new List<Coil>
					{
						new Coil { Address = 20, BoolValue = true },
						new Coil { Address = 21, BoolValue = false },
						new Coil { Address = 22, BoolValue = true },
						new Coil { Address = 23, BoolValue = true },
						new Coil { Address = 24, BoolValue = false },
						new Coil { Address = 25, BoolValue = false },
						new Coil { Address = 26, BoolValue = true },
						new Coil { Address = 27, BoolValue = true },
						new Coil { Address = 28, BoolValue = true },
						new Coil { Address = 29, BoolValue = false },
					};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 5, 12, 1, 2, 205, 1 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var coils = await client.ReadCoils(12, 20, 10);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, coils, "Response is incorrect");
		}

		[TestMethod]
		public async Task ClientReadDiscreteInputsTest()
		{
			// Function Code 0x02

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 1, 2, 0, 12, 0, 2 };
			var expectedResponse = new List<DiscreteInput>
			{
				new DiscreteInput { Address = 12, BoolValue = true },
				new DiscreteInput { Address = 13, BoolValue = true }
			};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 4, 1, 2, 1, 3 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var inputs = await client.ReadDiscreteInputs(1, 12, 2);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, inputs, "Response is incorrect");
		}

		[TestMethod]
		public async Task ClientReadHoldingRegisterTest()
		{
			// Function Code 0x03

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 5, 3, 0, 10, 0, 2 };
			var expectedResponse = new List<Register>
			{
				new Register { Address = 10, RegisterValue = 3, Type = ModbusObjectType.HoldingRegister },
				new Register { Address = 11, RegisterValue = 7, Type = ModbusObjectType.HoldingRegister }
			};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 7, 5, 3, 4, 0, 3, 0, 7 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var registers = await client.ReadHoldingRegisters(5, 10, 2);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, registers, "Response is incorrect");
		}

		[TestMethod]
		public async Task ClientReadInputRegisterTest()
		{
			// Function Code 0x04

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 3, 4, 0, 6, 0, 3 };
			var expectedResponse = new List<Register>
			{
				new Register { Address = 6, RegisterValue = 123, Type = ModbusObjectType.InputRegister },
				new Register { Address = 7, RegisterValue = 0, Type = ModbusObjectType.InputRegister },
				new Register { Address = 8, RegisterValue = 12345, Type = ModbusObjectType.InputRegister }
			};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 9, 3, 4, 6, 0, 123, 0, 0, 48, 57 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var registers = await client.ReadInputRegisters(3, 6, 3);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, registers, "Response is incorrect");
		}

		[TestMethod]
		public async Task ClientReadDeviceInformationBasicTest()
		{
			byte[] expectedRequest = new byte[] { 0, 0, 0, 5, 13, 43, 14, 1, 0 };
			var expectedResponse = new Dictionary<DeviceIDObject, string>
			{
				{ DeviceIDObject.VendorName, "AM.WD" },
				{ DeviceIDObject.ProductCode, "Mini-Test" },
				{ DeviceIDObject.MajorMinorRevision, "1.2.3.4" }
			};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");

					var bytes = new List<byte>();
					bytes.AddRange(request.Take(2));
					bytes.AddRange(new byte[] { 0, 0, 0, 0, 13, 43, 14, 1, 1, 0, 0, (byte)expectedResponse.Count });
					int len = 8;
					foreach (var kvp in expectedResponse)
					{
						byte[] b = Encoding.ASCII.GetBytes(kvp.Value);
						bytes.Add((byte)kvp.Key);
						len++;
						bytes.Add((byte)b.Length);
						len++;
						bytes.AddRange(b);
						len += b.Length;
					}
					bytes[5] = (byte)len;

					return bytes.ToArray();
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var deviceInfo = await client.ReadDeviceInformation(13, DeviceIDCategory.Basic);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, deviceInfo, "Response is incorrect");
		}

		[TestMethod]
		public async Task ClientReadDeviceInformationIndividualTest()
		{
			byte[] expectedRequest = new byte[] { 0, 0, 0, 5, 13, 43, 14, 4, (byte)DeviceIDObject.ModelName };
			var expectedResponse = new Dictionary<DeviceIDObject, string>
			{
				{ DeviceIDObject.ModelName, "TestModel" }
			};

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");

					var bytes = new List<byte>();
					bytes.AddRange(request.Take(2));
					bytes.AddRange(new byte[] { 0, 0, 0, 0, 13, 43, 14, 4, 2, 0, 0, (byte)expectedResponse.Count });
					int len = 8;
					foreach (var kvp in expectedResponse)
					{
						byte[] b = Encoding.ASCII.GetBytes(kvp.Value);
						bytes.Add((byte)kvp.Key);
						len++;
						bytes.Add((byte)b.Length);
						len++;
						bytes.AddRange(b);
						len += b.Length;
					}
					bytes[5] = (byte)len;

					return bytes.ToArray();
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var deviceInfo = await client.ReadDeviceInformation(13, DeviceIDCategory.Individual, DeviceIDObject.ModelName);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			CollectionAssert.AreEqual(expectedResponse, deviceInfo, "Response is incorrect");
		}

		#endregion Read

		#region Write

		[TestMethod]
		public async Task ClientWriteSingleCoilTest()
		{
			// Function Code 0x05

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 1, 5, 0, 173, 255, 0 };

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return request;
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var coil = new Coil
			{
				Address = 173,
				BoolValue = true
			};
			bool success = await client.WriteSingleCoil(1, coil);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			Assert.IsTrue(success);
		}

		[TestMethod]
		public async Task ClientWriteSingleRegisterTest()
		{
			// Function Code 0x06

			byte[] expectedRequest = new byte[] { 0, 0, 0, 6, 2, 6, 0, 5, 48, 57 };

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return request;
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var register = new Register
			{
				Type = ModbusObjectType.HoldingRegister,
				Address = 5,
				RegisterValue = 12345
			};
			bool success = await client.WriteSingleRegister(2, register);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			Assert.IsTrue(success);
		}

		[TestMethod]
		public async Task ClientWriteCoilsTest()
		{
			// Function Code 0x0F

			byte[] expectedRequest = new byte[] { 0, 0, 0, 9, 4, 15, 0, 20, 0, 10, 2, 205, 1 };

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 6, 4, 15, 0, 20, 0, 10 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var coils = new List<Coil>
					{
						new Coil { Address = 20, BoolValue = true },
						new Coil { Address = 21, BoolValue = false },
						new Coil { Address = 22, BoolValue = true },
						new Coil { Address = 23, BoolValue = true },
						new Coil { Address = 24, BoolValue = false },
						new Coil { Address = 25, BoolValue = false },
						new Coil { Address = 26, BoolValue = true },
						new Coil { Address = 27, BoolValue = true },
						new Coil { Address = 28, BoolValue = true },
						new Coil { Address = 29, BoolValue = false },
					};
			bool success = await client.WriteCoils(4, coils);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			Assert.IsTrue(success);
		}

		[TestMethod]
		public async Task ClientWriteRegistersTest()
		{
			// Function Code 0x10

			byte[] expectedRequest = new byte[] { 0, 0, 0, 11, 10, 16, 0, 2, 0, 2, 4, 0, 10, 1, 2 };

			using var server = new MiniTestServer
			{
				RequestHandler = (request, clientIp) =>
				{
					CollectionAssert.AreEqual(expectedRequest, request.Skip(2).ToArray(), "Request is incorrect");
					Console.WriteLine("Server sending response");
					return new byte[] { request[0], request[1], 0, 0, 0, 6, 10, 16, 0, 2, 0, 2 };
				}
			};
			server.Start();

			using var client = new ModbusClient(IPAddress.Loopback, server.Port, new ConsoleLogger());
			await client.Connect();
			Assert.IsTrue(client.IsConnected);

			var registers = new List<Register>
			{
				new Register { Address = 2, RegisterValue = 10, Type = ModbusObjectType.HoldingRegister },
				new Register { Address = 3, RegisterValue = 258, Type = ModbusObjectType.HoldingRegister }
			};
			bool success = await client.WriteRegisters(10, registers);
			Assert.IsTrue(string.IsNullOrWhiteSpace(server.LastError), server.LastError);
			Assert.IsTrue(success);
		}

		#endregion Write

		#endregion Modbus Client

		#region Modbus Server

		[TestMethod]
		public async Task ServerStartTest()
		{
			int port = 0;
			using (var testServer = new MiniTestServer())
			{
				testServer.Start();
				port = testServer.Port;
			}

			using var server = new ModbusServer(port);
			await server.Initialization;
			Assert.IsTrue(server.IsRunning);
		}

		#endregion Modbus Server

		#region TestServer

		internal delegate byte[] MiniTestServerRequestHandler(byte[] request, IPEndPoint endPoint);

		internal class MiniTestServer : IDisposable
		{
			private TcpListener listener;
			private CancellationTokenSource cts;

			private Task runTask;

			public MiniTestServer(int port = 0)
			{
				Port = port;
			}

			public int Port { get; private set; }

			public string LastError { get; private set; }

			public MiniTestServerRequestHandler RequestHandler { get; set; }

			public void Start()
			{
				cts = new CancellationTokenSource();

				listener = new TcpListener(IPAddress.Loopback, Port);
				listener.Start();

				Port = ((IPEndPoint)listener.LocalEndpoint).Port;

				Console.WriteLine("Server started: " + Port);
				runTask = Task.Run(() => RunServer(cts.Token));
			}

			public async Task Stop()
			{
				listener.Stop();
				cts.Cancel();
				await runTask;
				Console.WriteLine("Server stopped");
			}

			public void Dispose()
			{
				try
				{
					Stop().Wait();
				}
				catch
				{ }
			}

			private async Task RunServer(CancellationToken ct)
			{
				while (!ct.IsCancellationRequested)
				{
					try
					{
						var client = await listener.AcceptTcpClientAsync();
						try
						{
							var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;

							using var stream = client.GetStream();

							SpinWait.SpinUntil(() => stream.DataAvailable || ct.IsCancellationRequested);
							if (ct.IsCancellationRequested)
							{
								Console.WriteLine("Server cancel => WaitData");
								return;
							}

							byte[] buffer = new byte[100];
							var bytes = new List<byte>();
							do
							{
								int count = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
								bytes.AddRange(buffer.Take(count));
							}
							while (stream.DataAvailable && !ct.IsCancellationRequested);

							if (ct.IsCancellationRequested)
							{
								Console.WriteLine("Server cancel => DataRead");
								return;
							}

							Debug.WriteLine($"Server data read done: {bytes.Count} bytes");
							if (RequestHandler != null)
							{
								Console.WriteLine("Server send RequestHandler");
								try
								{
									byte[] response = RequestHandler(bytes.ToArray(), clientEndPoint);
									Console.WriteLine($"Server response: {response?.Length ?? -1}");
									if (response != null)
									{
										await stream.WriteAsync(response, 0, response.Length, ct);
										Console.WriteLine("Server response written");
									}
								}
								catch (Exception ex)
								{
									LastError = ex.GetMessage();
								}
							}
						}
						finally
						{
							client?.Dispose();
						}
					}
					catch (Exception ex)
					{
						string msg = ex.GetMessage();
						Console.WriteLine($"Server exception: " + msg);
					}
				}
			}
		}

		#endregion TestServer

		// Time for the scheduler to launch a thread to start the reconnect
		private async Task EnsureWait()
		{
			await Task.Delay(10);
		}
	}
}
