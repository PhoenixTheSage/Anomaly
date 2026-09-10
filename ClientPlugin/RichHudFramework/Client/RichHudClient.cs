using RichHudFramework.Internal;
using Sandbox.ModAPI;
using System;
using System.Reflection;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using ClientData = VRage.MyTuple<string, System.Action<int, object>, System.Action, int>;
using ServerData = VRage.MyTuple<System.Action, System.Func<int, object>, int>;

namespace RichHudFramework.Client
{
	using ExtendedClientData = MyTuple<ClientData, Action<Action>, ApiMemberAccessor>;

	/// <summary>
	/// API Client for the Rich HUD Framework.
	/// 
	/// This class handles the initialization and registration of a mod with the 
	/// Rich HUD Master module.
	/// </summary>
	public sealed class RichHudClient : RichHudComponentBase
	{
		internal static readonly Vector4I versionID = new Vector4I(1, 3, 0, 0); // Major, Minor, Rev, Hotfix
		internal const ClientSubtypes subtype = ClientSubtypes.Full;
		private const long modID = 1965654081, queueID = 1314086443;
		private const int vID = (int)APIVersionTable.Latest;

		/// <summary>
		/// Returns true if the client has been successfully registered with RichHudMaster.
		/// Use this check (e.g., in <c>Draw()</c> or <c>Update()</c>) before accessing 
		/// framework members externally to ensure the mod client is registered.
		/// </summary>
		public static bool Registered => Instance != null ? Instance.registered : false;

		private static RichHudClient Instance { get; set; }

		private readonly ExtendedClientData regMessage;
		private readonly Action InitAction, ResetAction;

		private bool regFail, registered, inQueue;
		private int nextPulseTick;
		private Func<int, object> GetApiDataFunc;
		private Action UnregisterAction;

		private RichHudClient(string modName, Action InitCallback, Action ResetCallback) : base(false, true)
		{
			InitAction = InitCallback;
			ResetAction = ResetCallback;

			ExceptionHandler.ModName = modName;

			var clientData = new ClientData(modName, MessageHandler, RemoteReset, vID);
			regMessage = new ExtendedClientData(clientData, ExceptionHandler.Run, GetOrSetMember);
		}

		/// <summary>
		/// Initialzes and registers the client with the API if it is not already registered.
		/// 
		/// This method should be called on session Init (e.g., from <c>MySessionComponentBase.Init()</c>) 
		/// of your main mod class.
		/// 
		/// *Important*: If your mod defines multiple session components, initialize the client 
		/// from **only one** to ensure proper behavior.
		/// </summary>
		/// <param name="modName">Name of the mod as it appears in the settings menu and in diagnostics.</param>
		/// <param name="InitCallback">Invoked upon successfully registering with the API. At this point, 
		/// it is safe to start using the framework.</param>
		/// <param name="ResetCallback">Invoked on client reset (unregistered). This occurs when 
		/// the game session is unloading, an unhandled exception is thrown on the client or master,
		/// or <c>RichHudClient.Reset()</c> is called manually.</param>
		public static void Init(string modName, Action InitCallback, Action ResetCallback)
		{
			if (Instance == null)
			{
				Instance = new RichHudClient(modName, InitCallback, ResetCallback);
				Instance.RequestRegistration();

				if (!Registered && !Instance.regFail)
				{
					Instance.EnterQueue();
				}
			}
		}

		/// <summary>
		/// Unregisters the client and resets all framework modules. 
		/// 
		/// This is used if you are designing your mod to be reloadable during the game session, 
		/// allowing you to reset the client before reinitializing your mod.
		/// </summary>
		public static void Reset()
		{
			if (Registered)
				ExceptionHandler.ReloadClients();
		}

		/// <summary>
		/// Re-sends the registration request if this client is still waiting on Master.
		/// Safe to call every tick. Pulsar .NET 10 can drop the queue ping or tear the
		/// client down without a world unload; this recovers without a reload.
		/// </summary>
		public static void Pulse()
		{
			RichHudClient inst = Instance;
			if (inst == null || inst.registered || inst.regFail)
				return;

			int now = Environment.TickCount;
			if (inst.nextPulseTick != 0 && unchecked(now - inst.nextPulseTick) < 2000)
				return;

			inst.RequestRegistration();
		}

		/// <summary>
		/// Handles registration response.
		/// </summary>
		private void MessageHandler(int typeValue, object message)
		{
			MsgTypes msgType = (MsgTypes)typeValue;

			if (!regFail)
			{
				if (!Registered)
				{
					Action unregister;
					Func<int, object> getApi;
					if (msgType == MsgTypes.RegistrationSuccessful && TryBindServerData(message, out unregister, out getApi))
					{
						UnregisterAction = unregister;
						GetApiDataFunc = getApi;

						registered = true;

						ExceptionHandler.Run(InitAction);
						ExceptionHandler.WriteToLog($"[RHF] Successfully registered with Rich HUD Master.");
					}
					else if (msgType == MsgTypes.RegistrationSuccessful)
					{
						ExceptionHandler.WriteToLog($"[RHF] RegistrationSuccessful payload was not ServerData (type: {message?.GetType().FullName ?? "null"}). Retrying.");
					}
					else if (msgType == MsgTypes.RegistrationFailed)
					{
						string text = message as string;
						if (text != null && text.IndexOf("already registered", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							// Master accepted an earlier send that this ALC could not bind.
							// Do not latch regFail — Pulse will retry after Master recycles the slot.
							ExceptionHandler.WriteToLog($"[RHF] Master already has this client. Waiting to bind API data.");
						}
						else
						{
							if (text != null)
								ExceptionHandler.WriteToLog($"[RHF] Failed to register with Rich HUD Master. Message: {text}");
							else
								ExceptionHandler.WriteToLog($"[RHF] Failed to register with Rich HUD Master.");

							regFail = true;
						}
					}
				}
			}
		}

		private object GetOrSetMember(object data, int memberEnum)
		{
			switch ((ClientDataAccessors)memberEnum)
			{
				case ClientDataAccessors.GetVersionID:
					return versionID;
				case ClientDataAccessors.GetSubtype:
					return subtype;
				case ClientDataAccessors.ReportException:
					return new Action<Exception>(ExceptionHandler.ReportException);
				case ClientDataAccessors.GetIsPausedFunc:
					return new Func<bool>(() => ExceptionHandler.ClientsPaused);
			}

			return null;
		}

		/// <summary>
		/// Attempts to register the client with the API.
		/// ExtendedClientData is a nested MyTuple; on CoreCLR / plugin ALCs Master's
		/// <c>is ExtendedClientData</c> can fail. Fall back to flat ClientData.
		/// </summary>
		private void RequestRegistration()
		{
			nextPulseTick = Environment.TickCount;
			MyAPIUtilities.Static.SendModMessage(modID, regMessage);
			if (!registered && !regFail)
				MyAPIUtilities.Static.SendModMessage(modID, regMessage.Item1);
		}

		/// <summary>
		/// Enters queue to await client registration.
		/// </summary>
		private void EnterQueue() =>
			MyAPIUtilities.Static.RegisterMessageHandler(queueID, QueueHandler);

		/// <summary>
		/// Unregisters callback for framework client queue.
		/// </summary>
		private void ExitQueue() =>
			MyAPIUtilities.Static.UnregisterMessageHandler(queueID, QueueHandler);

		/// <summary>
		/// Resend registration request on queue invocation.
		/// </summary>
		private void QueueHandler(object message)
		{
			if (!(registered || regFail))
			{
				inQueue = true;
				RequestRegistration();
			}
		}

		/// <summary>
		/// Internal update poll
		/// </summary>
		/// <exclude/>
		public override void Update()
		{
			if (registered && inQueue)
			{
				ExitQueue();
				inQueue = false;
			}
			else if (!registered && !regFail)
			{
				Pulse();
			}
		}

		/// <summary>
		/// Internal unload callback
		/// </summary>
		/// <exclude/>
		public override void Close()
		{
			ExitQueue();
			Unregister();
			Instance = null;
		}

		private void RemoteReset()
		{
			ExceptionHandler.Run(() =>
			{
				if (registered)
				{
					ExceptionHandler.ReloadClients();
					ResetAction();
				}
			});
		}

		/// <summary>
		/// Unregisters client from API
		/// </summary>
		private void Unregister()
		{
			if (registered)
			{
				registered = false;
				UnregisterAction();
			}
		}

		/// <summary>
		/// Master's ServerData is a VRage.MyTuple created in the workshop-mod load
		/// context. Pulsar .NET 10 plugin ALCs can make <c>is ServerData</c> fail even
		/// though Item1/Item2 are the unregister / GetApi delegates.
		/// </summary>
		private static bool TryBindServerData(object message, out Action unregister, out Func<int, object> getApi)
		{
			unregister = null;
			getApi = null;
			if (message == null)
				return false;

			if (message is ServerData)
			{
				var data = (ServerData)message;
				unregister = data.Item1;
				getApi = data.Item2;
				return unregister != null && getApi != null;
			}

			Type type = message.GetType();
			if (!type.IsValueType || !type.IsGenericType || type.Name != "MyTuple`3")
				return false;

			object item1 = GetTupleItem(type, message, "Item1");
			object item2 = GetTupleItem(type, message, "Item2");
			unregister = AsAction(item1);
			getApi = AsFuncIntObject(item2);
			return unregister != null && getApi != null;
		}

		private static object GetTupleItem(Type type, object tuple, string name)
		{
			FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
			if (field != null)
				return field.GetValue(tuple);
			PropertyInfo prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			return prop != null ? prop.GetValue(tuple, null) : null;
		}

		private static Action AsAction(object value)
		{
			Action action = value as Action;
			if (action != null)
				return action;
			Delegate d = value as Delegate;
			if (d == null)
				return null;
			return () => d.DynamicInvoke();
		}

		private static Func<int, object> AsFuncIntObject(object value)
		{
			Func<int, object> func = value as Func<int, object>;
			if (func != null)
				return func;
			Delegate d = value as Delegate;
			if (d == null)
				return null;
			return id => d.DynamicInvoke(id);
		}

		/// <summary>
		/// Base class for types acting as modules for the API
		/// </summary>
		/// <exclude/>
		public abstract class ApiModule : RichHudComponentBase
		{
			protected readonly ApiModuleTypes componentType;

			public ApiModule(ApiModuleTypes componentType, bool runOnServer, bool runOnClient) : base(runOnServer, runOnClient)
			{
				if (!Registered)
					throw new Exception("Types of ApiModule cannot be instantiated before RichHudClient is initialized.");

				this.componentType = componentType;
			}

			protected object GetApiData()
			{
				return Instance?.GetApiDataFunc((int)componentType);
			}
		}
	}
}