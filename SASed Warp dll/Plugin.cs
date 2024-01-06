
using System;
using System.Reflection;
using System.Collections.Concurrent;

using BepInEx;

using SpaceWarp;
using SpaceWarp.API.Mods;
using SpaceWarp.API.Assets;
using SpaceWarp.API.UI.Appbar;

using KSP.Sim;
using KSP.Sim.impl;
using KSP.Game;
using KSP.Messages;

using HarmonyLib;

namespace SASedWarp
{

	[BepInPlugin("SASed-Warp", "SASed Warp", "1.0")]
	[BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]
	public class SASedWarpPlugin : BaseSpaceWarpPlugin
	{
		private bool in_valid_game_state = false;
		private bool menu_open = false;

		public SASedWarpPlugin() { }

		#region Init

		public override void OnPreInitialized()
		{
			base.OnPreInitialized();
		}

		public override void OnInitialized()
		{

			#region GameStateChangedMessage=>in_valid_game_state
			Game.Messages.Subscribe<GameStateChangedMessage>(msg =>
			{
				var message = (GameStateChangedMessage)msg;

				switch (message.CurrentState)
				{
					case GameState.WarmUpLoading:
					case GameState.MainMenu:
					case GameState.KerbalSpaceCenter:
					case GameState.VehicleAssemblyBuilder:
					case GameState.BaseAssemblyEditor:
					case GameState.ColonyView:
					case GameState.PhotoMode:
					case GameState.MetricsMode:
					case GameState.PlanetViewer:
					case GameState.Loading:
					case GameState.TrainingCenter:
					case GameState.MissionControl:
					case GameState.TrackingStation:
					case GameState.ResearchAndDevelopment:
					case GameState.Launchpad:
					case GameState.Runway:
					case GameState.Flag:
						in_valid_game_state = false;
						break;
					case GameState.FlightView:
					case GameState.Map3DView:
						in_valid_game_state = true;
						break;
					default:
						throw new NotImplementedException(message.CurrentState.ToString());
				}
			});
			#endregion

			Appbar.RegisterAppButton(
				"SASed Warp",
				"BTN-SASed-Warp",
				AssetManager.GetAsset<UnityEngine.Texture2D>($"{SpaceWarpMetadata.ModID}/images/icon.png"),
				open =>
				{
					menu_open = open;
				}
			);

			VesselComponent_HandleOrbitalPhysicsUnderThrustStart_Patch.log = Logger;
			// For now - skip this patch, it ended up being unnecessary
			//Harmony.CreateAndPatchAll(typeof(SASedWarpPlugin).Assembly);

			base.OnInitialized();
		}

		public override void OnPostInitialized()
		{
			base.OnPostInitialized();
		}

		#endregion

		#region Update

		private static readonly BindingFlags AllBF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		public void Update()
		{
			if (!in_valid_game_state) return;
			if (!menu_open) return;
			if (!GameManager.Instance.Game.ViewController.TimeWarp.IsWarping) return;
			if (GameManager.Instance.Game.ViewController.TimeWarp.IsPhysicsTimeWarp) return;

			var vessel = GameManager.Instance.Game.ViewController.GetActiveSimVessel(true);
			if (vessel.flightCtrlState.mainThrottle==0) return;
			// "left"??? it somehow spin-stabilizes direction during warp, lol
			// "up" only works for ~90 degrees of orbit
			// And "forward", the most expected thing, rotates 90 degrees up from targ_forward. But unlike "up" it doesn't glich out after a while
			// Makes me question if I actually know linear algebra... But prob just bad naming of methods
			var curr_forward = vessel.transform.left;

			var autopilot = vessel.Autopilot;
			var telemetry = (TelemetryComponent)autopilot.GetType().GetField("_telemetry", AllBF).GetValue(autopilot);
			Vector targ_forward;
			switch (autopilot.AutopilotMode)
			{
				case AutopilotMode.StabilityAssist:
				case AutopilotMode.Maneuver:
					return;
				case AutopilotMode.Prograde:
					targ_forward = telemetry.OrbitMovementPrograde;
					break;
				case AutopilotMode.Retrograde:
					targ_forward = telemetry.OrbitMovementRetrograde;
					break;
				case AutopilotMode.Normal:
					targ_forward = telemetry.OrbitMovementNormal;
					break;
				case AutopilotMode.Antinormal:
					targ_forward = telemetry.OrbitMovementAntiNormal;
					break;
				case AutopilotMode.RadialIn:
					targ_forward = telemetry.OrbitMovementRadialIn;
					break;
				case AutopilotMode.RadialOut:
					targ_forward = telemetry.OrbitMovementRadialOut;
					break;
				case AutopilotMode.Target:
					targ_forward = telemetry.TargetDirection;
					break;
				case AutopilotMode.AntiTarget:
					targ_forward = telemetry.AntiTargetDirection;
					break;
					//TODO What are these?
				//case AutopilotMode.Navigation:
				//	break;
				//case AutopilotMode.Autopilot:
				//	break;
				default:
					throw new NotImplementedException(autopilot.AutopilotMode.ToString());
			}
			
			//Logger.LogDebug($"Vector diff sqr mag={(curr_forward - targ_forward).sqrMagnitude}");
			//if ((curr_forward - targ_forward).sqrMagnitude==0) return; //TODO <0.01

			// I would expect .FromTo to do the thing
			// But besides rotating the craft, it also makes camera wonky
			// And .LookRotation just doesn't do that
			//var rotation = Rotation.FromTo(curr_forward, targ_forward);
			var rotation = Rotation.LookRotation(curr_forward, targ_forward);
			
			//Logger.LogDebug($"Rotating from forward={curr_forward.vector}");
			//Logger.LogDebug($"Towards prograde={curr_forward.coordinateSystem.ToLocalVector(targ_forward)}");
			//Logger.LogDebug($"Using rotation={rotation.localRotation}");
			vessel.transform.UpdateRotation(rotation);

			// Recalculate the trajectory using new vessel rotation
			//var sw = System.Diagnostics.Stopwatch.StartNew();
			vessel.GetType().GetMethod("HandleOrbitalPhysicsUnderThrustStart", AllBF).Invoke(vessel, null);
			//Logger.LogDebug($"Recalculated thrust-warp trajectory in {sw.Elapsed}");

			if (vessel.IsOrbitalPhysicsUnderThrustActive) return;
			// If not, there was an error in KSP code (look in Alt+C),
			// at least cancel thrust so it doesn't burn propelant
			var vehicle = (VesselVehicle)GameManager.Instance.Game.ViewController.GetActiveVehicle(true);
			vehicle.AtomicSet(mainThrottle: 0, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

			//TODO White box instead of proper message?
			GameManager.Instance.Game.Notifications.ProcessNotification(new NotificationData
			{
				Tier = NotificationTier.Alert,
				Primary = new NotificationLineItemData { LocKey = "SASedWarp/MathBug" }
			});

		}

		#endregion

	}

	[HarmonyPatch(typeof(VesselComponent), "HandleOrbitalPhysicsUnderThrustStart", MethodType.Normal)]
	public class VesselComponent_HandleOrbitalPhysicsUnderThrustStart_Patch
	{
		private static readonly ConcurrentDictionary<VesselComponent, byte> all_instances = new ConcurrentDictionary<VesselComponent, byte>();
		public static BepInEx.Logging.ManualLogSource log = null!; // set in SASedWarpPlugin.OnInitialized

		public static void Prefix(
			VesselComponent __instance
		)
		{
			var added = all_instances.TryAdd(__instance, 0);
			log.LogDebug($"HandleOrbitalPhysicsUnderThrustStart: new_inst={added}, total_inst={all_instances.Count}");
		}

	}

}
