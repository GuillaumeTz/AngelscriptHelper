using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AngelScriptHelper
{
	class CUtils
	{
		public static void MemSet(byte[] array, byte value)
		{
			if (array == null)
			{
				throw new ArgumentNullException("array");
			}
			const int blockSize = 4096; // bigger may be better to a certain extent
			int index = 0;
			int length = Math.Min(blockSize, array.Length);
			while (index < length)
			{
				array[index++] = value;
			}
			length = array.Length;
			while (index < length)
			{
				Buffer.BlockCopy(array, 0, array, index, Math.Min(blockSize, length - index));
				index += blockSize;
			}
		}
	}

	enum EMessageType
	{
		Diagnostics = 0,
		RequestDebugDatabase,
		DebugDatabase,

		StartDebugging,
		StopDebugging,
		Pause,
		Continue,

		RequestCallStack,
		CallStack,

		ClearBreakpoints,
		SetBreakpoint,

		HasStopped,
		HasContinued,

		StepOver,
		StepIn,
		StepOut,

		EngineBreak,

		RequestVariables,
		Variables,

		RequestEvaluate,
		Evaluate,
		GoToDefinition,

		BreakOptions,
		RequestBreakFilters,
		BreakFilters,

		Disconnect,

		DebugDatabaseFinished,
		AssetDatabaseInit,
		AssetDatabase,
		AssetDatabaseFinished,
		FindAssets,
		DebugDatabaseSettings,

		PingAlive,

		DebugServerVersion,
		CreateBlueprint,

		ReplaceAssetDefinition,

		SetDataBreakpoints,
		ClearDataBreakpoints,
	}

	class CMessage
	{
		public EMessageType mType;
		private readonly MemoryStream mStream;
		private readonly BinaryReader mBinaryReader;
		public CMessage(EMessageType InType, MemoryStream InBuffer)
		{
			mType = InType;
			mStream = InBuffer;
			mBinaryReader = new BinaryReader(mStream, Encoding.UTF8);
		}

		public int ReadInt() { return mBinaryReader.ReadInt32(); }
		public bool ReadBool() 
		{ 
			return mBinaryReader.ReadInt32() != 0; 
		}

		public string ReadString() 
		{
			int Length = mBinaryReader.ReadInt32();
			string result = new string(mBinaryReader.ReadChars(Length));
			return result.Remove(result.Length - 1);
		}
	}

	enum EDiagnosticSeverity
	{
		Information,
		Warning,
		Error
	}

	struct STextLocation
	{
		public int LineNumber;
		public int CharacterInLine;
	}

	class CDiagnosticsMessage
	{
		public string FilePath = string.Empty;
		public List<CAngelscriptDiagnostic> Diagnostics = new List<CAngelscriptDiagnostic>();
	}

	class CAngelscriptDiagnostic
	{
		public EDiagnosticSeverity severity;
		public STextLocation Start;
		public STextLocation End;
		public string message;
		public string source;

		public static CDiagnosticsMessage ReadDiagnostics(CMessage Message)
		{
			CDiagnosticsMessage Result = new CDiagnosticsMessage();

			Result.FilePath = Message.ReadString();
			Result.FilePath = Result.FilePath.Replace("\\", "/");
			Result.FilePath = Result.FilePath.Replace("//", "/");

			int msgCount = Message.ReadInt();
			for (int i = 0; i < msgCount; ++i)
			{
				string message = Message.ReadString();
				int line = Message.ReadInt();
				int charNum = Message.ReadInt();
				bool isError = Message.ReadBool();
				bool isInfo = Message.ReadBool();

				if (line <= 0)
					line = 1;

				CAngelscriptDiagnostic diagnostic = new CAngelscriptDiagnostic();
				diagnostic.severity = isInfo ? EDiagnosticSeverity.Information : (isError ? EDiagnosticSeverity.Error : EDiagnosticSeverity.Warning);
				diagnostic.Start = new STextLocation { LineNumber = line - 1, CharacterInLine = 0 };
				diagnostic.End = new STextLocation { LineNumber = line - 1, CharacterInLine = 10000 };
				diagnostic.message = message;
				diagnostic.source = "as";

				Result.Diagnostics.Add(diagnostic);
			};

			return Result;
		}
	};

	class CDebugClient
	{
		private TcpClient mSocketTcpClient = null;
		private NetworkStream mSocketStream = null;
		private DateTime mLastTimeCheckedAlive = DateTime.Now;
		private bool bIsConnecting = false;

		public Dictionary<string, CDiagnosticsMessage> DiagnosticsMessageMap = new Dictionary<string, CDiagnosticsMessage>();
		public event EventHandler OnDiagnosticsChanged = null;

		private bool bDiagnosticsDirty = false;
		private DateTime mLastTimeDiagnosticsDirty = DateTime.Now;

		public void Connect(string HostName, int Port, AsyncCallback OnConnected)
		{
			Disconnect(true);

			try
			{
				bIsConnecting = true;
				mSocketTcpClient = new TcpClient();
				mSocketTcpClient.BeginConnect(HostName, Port, (IAsyncResult AsyncResult) => { OnConnectedAsyncCallback(AsyncResult); OnConnected?.Invoke(AsyncResult); }, null);
			}
			catch (System.Exception)
			{
			}
		}
		private void OnConnectedAsyncCallback(IAsyncResult AsyncResult)
		{
			try
			{
				mSocketTcpClient.EndConnect(AsyncResult);
			}
			catch (System.Exception)
			{
			}

			bIsConnecting = false;
			if (mSocketTcpClient.Connected)
			{
				mSocketStream = mSocketTcpClient.GetStream();
				mSocketStream.ReadTimeout = 1;
			}
		}

		public bool IsConnected() 
		{
			try
			{
				if (mSocketTcpClient == null || !mSocketTcpClient.Connected || mSocketStream == null)
					return false;

				return !(mSocketTcpClient.Client.Poll(1000, SelectMode.SelectRead) && mSocketTcpClient.Client.Available == 0);
			}
			catch (System.Exception)
			{

			}

			return false;
		}

		public bool IsConnecting()
		{
			return bIsConnecting;
		}

		public void Tick()
		{
			if (!IsConnected())
			{
				Disconnect(false);
				return;
			}

			if ((DateTime.Now - mLastTimeCheckedAlive).Seconds > 5)
			{
				mLastTimeCheckedAlive = DateTime.Now;
				Send(EMessageType.PingAlive);
			}

			while (mSocketStream.DataAvailable)
			{
				mLastTimeCheckedAlive = DateTime.Now;

				const int MaxBufferSize = 1024 * 1024 * 4;
				byte[] ReadBuffer = new byte[MaxBufferSize];
				CUtils.MemSet(ReadBuffer, 0);
				int NumReceived = 0;
				while (NumReceived < 4)
				{
					NumReceived += mSocketStream.Read(ReadBuffer, NumReceived, 4 - NumReceived);
				}

				int PacketExpectedSize = (ReadBuffer[0] | (ReadBuffer[1] << 8) | (ReadBuffer[2] << 16) | (ReadBuffer[3] << 24)) + 1;
				if (PacketExpectedSize <= 0 || PacketExpectedSize >= MaxBufferSize)
					return;

				int Offset = 0;
				ReadBuffer[0] = ReadBuffer[1] = ReadBuffer[2] = ReadBuffer[3] = 0;
				while (Offset < PacketExpectedSize)
				{
					Offset += mSocketStream.Read(ReadBuffer, Offset, PacketExpectedSize - Offset);
				}

				MemoryStream Stream = new MemoryStream(ReadBuffer, 0, Offset, false);
				EMessageType MessageType = (EMessageType)(Stream.ReadByte());
				HandleMessage(MessageType, new CMessage(MessageType, Stream));
			}

			// Wait a little to emit event diagnostics changed
			if (bDiagnosticsDirty && (DateTime.Now - mLastTimeDiagnosticsDirty).Milliseconds > 250 && OnDiagnosticsChanged != null)
			{
				OnDiagnosticsChanged.Invoke(this, null);
				bDiagnosticsDirty = false;
			}
		}

		void HandleMessage(EMessageType MessageType, CMessage Message)
		{
			switch (MessageType)
			{
				case EMessageType.PingAlive:
					{
						Send(EMessageType.PingAlive);
						break;
					}
				case EMessageType.Diagnostics:
					{
						var DiagnosticsMessage = CAngelscriptDiagnostic.ReadDiagnostics(Message);
						DiagnosticsMessageMap.Remove(DiagnosticsMessage.FilePath);
						DiagnosticsMessageMap.Add(DiagnosticsMessage.FilePath, DiagnosticsMessage);
						bDiagnosticsDirty = true;
						mLastTimeDiagnosticsDirty = DateTime.Now;
						break;
					}
				case EMessageType.Disconnect:
					{
						Disconnect(false);
						break;
					}
			}
		}

		public void Send(EMessageType messageType)
		{
			try
			{
				BinaryWriter lBinaryWriter = new BinaryWriter(mSocketStream, Encoding.UTF8);
				lBinaryWriter.Write((int)1);
				lBinaryWriter.Write((byte)(messageType));
			}
			catch (System.Exception)
			{
				Disconnect(false);
			}
		}

		public void Disconnect(bool bNotify)
		{
			bool bHasValue = DiagnosticsMessageMap.Keys.Count > 0;
			DiagnosticsMessageMap.Clear();
			try
			{
				if (bHasValue)
					OnDiagnosticsChanged?.Invoke(this, null);

				if (IsConnected() && bNotify)
				{
					Send(EMessageType.Disconnect);
				}

				if (mSocketTcpClient != null)
				{
					mSocketTcpClient.Close();
				}
			}
			catch (Exception)
			{
				
			}

			bIsConnecting = false;
			bDiagnosticsDirty = false;
			DiagnosticsMessageMap.Clear();
			mSocketTcpClient = null;
			mSocketStream = null;
		}
	}

	class CAngelScriptManager
	{
		private readonly CDebugClient mDebugClient = new CDebugClient();

		private static CAngelScriptManager mInstance = null;

		private DateTime LastTimeTryConnect = DateTime.Now;
		private int CurentDelayBetweenTicks = 5000;

		public event EventHandler OnDiagnosticsChanged = null;
		private bool bRunning = false;

		private CAngelScriptManager()
		{
			mDebugClient.OnDiagnosticsChanged += (s, e) =>
			{
				HandleDiagnosticsChanged(e);
			};

			bRunning = true;
			ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
			{
				await Task.Run(() => MainLoop());
			});
		}

		public static void Init()
		{
			mInstance = new CAngelScriptManager();
		}

		public static CAngelScriptManager Instance()
		{
			return mInstance;
		}
		void HandleDiagnosticsChanged(EventArgs e)
		{
			OnDiagnosticsChanged?.Invoke(this, e);

			// launch a task on the main ui thread
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

				CErrorListHelper.Instance().ClearErrors();
				bool bHasErrors = false;
				foreach (KeyValuePair<string, CDiagnosticsMessage> DiagnosticPair in mDebugClient.DiagnosticsMessageMap)
				{
					foreach (CAngelscriptDiagnostic Diagnostic in DiagnosticPair.Value.Diagnostics)
					{
						bHasErrors = true;
						CErrorListHelper.Instance().AddError(Diagnostic.message, DiagnosticPair.Value.FilePath, Diagnostic.Start.LineNumber, Diagnostic.Start.CharacterInLine);
					}
				}

				if (bHasErrors)
				{
					CErrorListHelper.Instance().ShowErrorList();
				}
			});
		}

		public Dictionary<string, CDiagnosticsMessage>  GetDiagnosticsMessageMap()
		{
			return mDebugClient.DiagnosticsMessageMap;
		}

		void MainLoop()
		{
			while (bRunning)
			{
				Thread.Sleep(CurentDelayBetweenTicks);
				Tick();
			}
		}

		void Tick()
		{
			try
			{
				if (!mDebugClient.IsConnected())
				{
					CurentDelayBetweenTicks = 5000;
					// Detect if the UnrealEditor.exe process is running
					bool isUnrealEditorRunning = false;
					try
					{
						System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("UnrealEditor");
						isUnrealEditorRunning = processes != null && processes.Length > 0;
					}
					catch (Exception)
					{
					}

					if (isUnrealEditorRunning && !mDebugClient.IsConnecting())
					{
						mDebugClient.Connect("127.0.0.1", 27099, OnDebugClientConnected);
					}
				}
				else
				{
					mDebugClient.Tick();
				}
			}
			catch (Exception)
			{
				mDebugClient.Disconnect(false);
			}
		}
		void OnDebugClientConnected(IAsyncResult _)
		{
			CurentDelayBetweenTicks = 200;
		}
	}

	
}
