using Microsoft.VisualStudio.RpcContracts.DiagnosticManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace AngelScriptHelper
{
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
		private MemoryStream mStream;
		private BinaryReader mBinaryReader;
		public CMessage(EMessageType InType, MemoryStream InBuffer)
		{
			mType = InType;
			mStream = InBuffer;
			mBinaryReader = new BinaryReader(mStream, Encoding.UTF8);
		}

		public int readInt() { return mBinaryReader.ReadInt32(); }
		public bool readBool() 
		{ 
			return mBinaryReader.ReadInt32() != 0; 
		}

		public string readString() 
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

			Result.FilePath = Message.readString();

			Result.FilePath = Result.FilePath.Replace("\\", "/");
			Result.FilePath = Result.FilePath.Replace("//", "/");

			int msgCount = Message.readInt();
			for (int i = 0; i < msgCount; ++i)
			{
				var message = Message.readString();
				var line = Message.readInt();
				var charNum = Message.readInt();
				var isError = Message.readBool();
				var isInfo = Message.readBool();

				if (line <= 0)
					line = 1;

				var diagnostic = new CAngelscriptDiagnostic();
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
		private TcpClient mSocket = null;
		private NetworkStream mSocketStream = null;
		private DateTime mLastTimeCheckedAlive = DateTime.Now;

		public Dictionary<string, CDiagnosticsMessage> DiagnosticsMessageMap = new Dictionary<string, CDiagnosticsMessage>();
		public event EventHandler OnDiagnosticsChanged;

		private bool bDiagnosticsDirty = false;
		private DateTime mLastTimeDiagnosticsDirty = DateTime.Now;

		public void Connect(string HostName, int Port)
		{
			Disconnect(true);

			try
			{
				mSocket = new TcpClient(HostName, Port);
				mSocketStream = mSocket.GetStream();
				mSocketStream.ReadTimeout = 1;
			}
			catch (System.Exception)
			{
				
			}
		}

		public bool IsConnected() 
		{
			if (mSocket == null || !mSocket.Connected)
				return false;

			return !(mSocket.Client.Poll(1000, SelectMode.SelectRead) && mSocket.Client.Available == 0);
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

				byte[] ReadBuffer = new byte[1024 * 1024];
				int NumReceived = 0;
				while (NumReceived < 4)
				{
					try
					{
						NumReceived += mSocketStream.Read(ReadBuffer, NumReceived, 4 - NumReceived);
					}
					catch (System.Exception)
					{
						NumReceived = -1;
					}

					if (NumReceived < 0)
						return;
				}

				int PacketExpectedSize = (ReadBuffer[0] | (ReadBuffer[1] << 8) | (ReadBuffer[2] << 16) | (ReadBuffer[3] << 24)) + 1;

				if (PacketExpectedSize <= 0 || PacketExpectedSize > 1024 * 1024)
					return;

				int Offset = 0;
				while (Offset < PacketExpectedSize)
				{
					Offset += mSocketStream.Read(ReadBuffer, Offset, PacketExpectedSize - Offset);
				}

				MemoryStream Stream = new MemoryStream(ReadBuffer, 0, Offset, false);
				EMessageType MessageType = (EMessageType)(Stream.ReadByte());
				HandleMessage(MessageType, new CMessage(MessageType, Stream));
			}

			// Wait a little to emit event diagnostics changed
			if (bDiagnosticsDirty && (DateTime.Now - mLastTimeDiagnosticsDirty).Seconds > 1)
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
			if (mSocket == null)
				return;

			if (bNotify)
				Send(EMessageType.Disconnect);

			mSocketStream = null;
			mSocket = null;
		}
	}

	class CAngelScriptManager
	{
		public CDebugClient mDebugClient;

		private static CAngelScriptManager mInstance = null;
		private static SpinLock mInstanceLock = new SpinLock();
		private static SpinLock mLock = new SpinLock();

		DateTime LastTimeTryConnect = DateTime.Now;

		public event EventHandler OnDiagnosticsChanged;

		System.Timers.Timer TickTimer;

		private CAngelScriptManager()
		{
			TickTimer = new System.Timers.Timer();
			TickTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
			TickTimer.Interval = 100;
			TickTimer.AutoReset = false;
			TickTimer.Start();

			mDebugClient = new CDebugClient();
			mDebugClient.OnDiagnosticsChanged += (s, e) => { HandleDiagnosticsChanged(e); };
		}

		public static CAngelScriptManager Instance()
		{
			bool bEntered = false;
			try
			{
				mInstanceLock.Enter(ref bEntered);
				if (mInstance == null)
				{
					mInstance = new CAngelScriptManager();
				}
			}
			finally
			{
				if (bEntered)
					mInstanceLock.Exit();
			}

			return mInstance;
		}

		static void OnTimedEvent(object source, ElapsedEventArgs e)
		{
			Instance().Tick();
		}

		void HandleDiagnosticsChanged(EventArgs e)
		{
			if (OnDiagnosticsChanged != null) 
				OnDiagnosticsChanged.Invoke(this, e);

			CErrorListHelper.Instance().ClearErrors();
			foreach (KeyValuePair<string, CDiagnosticsMessage> DiagnosticPair in mDebugClient.DiagnosticsMessageMap)
			{
				foreach (CAngelscriptDiagnostic Diagnostic in DiagnosticPair.Value.Diagnostics)
				{
					CErrorListHelper.Instance().AddError(Diagnostic.message, DiagnosticPair.Value.FilePath, Diagnostic.Start.LineNumber, Diagnostic.Start.CharacterInLine);
				}
			}
			CErrorListHelper.Instance().ShowErrorList();
		}

		void Tick()
		{
			bool bEntered = false;
			try
			{
				mLock.TryEnter(ref bEntered);
				if (bEntered)
				{
					if (!mDebugClient.IsConnected())
					{
						if ((DateTime.Now - LastTimeTryConnect).Seconds >= 1)
						{
							LastTimeTryConnect = DateTime.Now;
							mDebugClient.Connect("127.0.0.1", 27099);
						}
					}
					else
					{
						mDebugClient.Tick();
					}
				}
			}
			finally
			{
				if (bEntered)
					mLock.Exit();
			}

			TickTimer.Start();
		}
	}

	
}
