namespace SASedWarp;



using System;

using System.Reflection;

using KSP.Sim;
using KSP.Sim.impl;
using KSP.Game;
using KSP.Messages;

using BepInEx;
using BepInEx.Configuration;

using SpaceWarp;
using SpaceWarp.API.Mods;
using SpaceWarp.API.Assets;
using SpaceWarp.API.UI.Appbar;

using HarmonyLib;

using JetBrains.Annotations;



[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]
public class SASedWarpPlugin : BaseSpaceWarpPlugin
{
	[PublicAPI] public const string ModGuid = MyPluginInfo.PLUGIN_GUID;
	[PublicAPI] public const string ModName = MyPluginInfo.PLUGIN_NAME;
	[PublicAPI] public const string ModVer = MyPluginInfo.PLUGIN_VERSION;

	[PublicAPI] public static SASedWarpPlugin Instance { get; set; } = null!;

	private const string ToolbarFlightButtonID = "BTN-SASedWarpFlight";

	private bool in_valid_game_state = false;
	private bool menu_active = false;

	private readonly ConfigEntry<bool> c_direction_lines, c_h_to_snap;

	#region Init

	public SASedWarpPlugin()
	{
		c_direction_lines = Config.Bind(
			MyPluginInfo.PLUGIN_NAME,
			"Funny debug lines",
			false,
			"Red is up and then forward in control part space\n"+
			"Green is up and then forward in vessel space\n"+
			"Blue is orbinal speed prograde and then orbital speed radial out"
		);
		c_h_to_snap = Config.Bind(
			MyPluginInfo.PLUGIN_NAME,
			"Press H to snap",
			false,
			"Only activate direction change (to SAS during warp) once for every press of H key, instead of on every update"
		);
	}

	public override void OnInitialized()
	{
		base.OnInitialized();
		Instance = this;

		#region GameStateChangedMessage=>in_valid_game_state

		Game.Messages.Subscribe<GameStateChangedMessage>(msg =>
		{
			var message = (GameStateChangedMessage)msg;

			in_valid_game_state = message.CurrentState switch
			{

				GameState.WarmUpLoading => false,
				GameState.MainMenu => false,
				GameState.KerbalSpaceCenter => false,
				GameState.VehicleAssemblyBuilder => false,
				GameState.BaseAssemblyEditor => false,
				GameState.ColonyView => false,
				GameState.PhotoMode => false,
				GameState.MetricsMode => false,
				GameState.PlanetViewer => false,
				GameState.Loading => false,
				GameState.TrainingCenter => false,
				GameState.MissionControl => false,
				GameState.TrackingStation => false,
				GameState.ResearchAndDevelopment => false,
				GameState.Launchpad => false,
				GameState.Runway => false,
				GameState.Flag => false,

				GameState.FlightView => true,
				GameState.Map3DView => true,

				_ => throw new NotImplementedException(message.CurrentState.ToString()),
			};
		});

		#endregion

		// Register Flight AppBar button
		Appbar.RegisterAppButton(
			ModName,
			ToolbarFlightButtonID,
			AssetManager.GetAsset<UnityEngine.Texture2D>($"{ModGuid}/images/icon.png"),
			is_open => menu_active = is_open
		);

		UnityEngine.Camera.onPreRender += cam =>
		{
			if (!c_direction_lines.Value) return;
			if (!in_valid_game_state) return;
			if (!menu_active) return;

			using var c = Shapes.Draw.Command(cam, UnityEngine.Rendering.CameraEvent.AfterImageEffectsOpaque);

			var vessel = GameManager.Instance.Game.ViewController.GetActiveSimVessel(true);
			var telemetry = vessel.SimulationObject.Telemetry;
			var frame = vessel.transform.coordinateSystem;
			var pos = frame.ToLocalPosition(vessel.CenterOfMass);
			var r = 2 * GameManager.Instance.Game.SpaceSimulation.ModelViewMap.FromModel(vessel.SimulationObject).Vessel.BoundingSphere.radius;

			void DrawDir(Position p, Vector v1, Vector v2, UnityEngine.Color c)
			{
				var p0 = p;
				var p1 = p0 + v1*r;
				var p2 = p1 + v2*r*0.1;
				Shapes.Draw.Line(frame.ToLocalPosition(p0), frame.ToLocalPosition(p1), r*0.01f, Shapes.LineEndCap.Square, c);
				Shapes.Draw.Line(frame.ToLocalPosition(p1), frame.ToLocalPosition(p2), r*0.01f, Shapes.LineEndCap.Square, c);
				//Logger.LogDebug($"{c}: {v1.magnitude}");
			}

			DrawDir(vessel.ControlTransform.Position, vessel.ControlTransform.up, vessel.ControlTransform.forward, UnityEngine.Color.red);
			DrawDir(vessel.CenterOfMass, vessel.transform.up, vessel.transform.forward, UnityEngine.Color.green);
			DrawDir(vessel.CenterOfMass, telemetry.OrbitMovementPrograde, telemetry.OrbitMovementRadialOut, UnityEngine.Color.blue);

			//DrawDir(vessel.CenterOfMass, new Vector(vessel.ControlTransform.Rotation.coordinateSystem, vessel.transform.Rotation.coordinateSystem.ToLocalVector(telemetry.OrbitMovementPrograde)), default, UnityEngine.Color.yellow);
			//DrawDir(vessel.CenterOfMass, new Vector(vessel.transform.Rotation.coordinateSystem, vessel.ControlTransform.Rotation.coordinateSystem.ToLocalVector(telemetry.OrbitMovementPrograde)), default, UnityEngine.Color.cyan);

		};

		UnityEngine.Camera.onPostRender += cam =>
		{
			if (!c_direction_lines.Value) return;
			if (!in_valid_game_state) return;
			if (!menu_active) return;

			// HUD (the mod) used this, but it's private
			//Shapes.DrawCommand.OnPostRenderBuiltInRP(cam);

		};
		
		Harmony.CreateAndPatchAll(typeof(SASedWarpPlugin).Assembly);

	}

	#endregion

	#region Update
	
	private static readonly BindingFlags AllBF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private bool h_down = false;

	public void Update()
	{

		#region Checks and init

		var old_h_down = h_down;
		h_down = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.H);
		if (c_h_to_snap.Value)
		{
			if (h_down || !old_h_down) return;
		}

		if (!in_valid_game_state) return;
		if (!menu_active) return;
		if (!GameManager.Instance.Game.ViewController.TimeWarp.IsWarping) return;
		if (GameManager.Instance.Game.ViewController.TimeWarp.IsPhysicsTimeWarp) return;
		var vessel = GameManager.Instance.Game.ViewController.GetActiveSimVessel(true);

		var autopilot = vessel.Autopilot;
		if (autopilot.AutopilotMode == AutopilotMode.StabilityAssist) return;
		var telemetry = vessel.SimulationObject.Telemetry;

		#endregion

		#region Find the vector representing the SAS direction

		Vector targ_forward = autopilot.AutopilotMode switch
		{
			AutopilotMode.Maneuver => telemetry.ManeuverDirection,
			AutopilotMode.Target => telemetry.TargetDirection,
			AutopilotMode.AntiTarget => telemetry.AntiTargetDirection,
			AutopilotMode.Prograde => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementPrograde,
				SpeedDisplayMode.Target => telemetry.TargetPrograde,
				SpeedDisplayMode.Surface => telemetry.SurfaceMovementPrograde,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			AutopilotMode.Retrograde => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementRetrograde,
				SpeedDisplayMode.Target => telemetry.TargetRetrograde,
				SpeedDisplayMode.Surface => telemetry.SurfaceMovementRetrograde,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			AutopilotMode.RadialIn => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementRadialIn,
				SpeedDisplayMode.Target => telemetry.HorizonDown,
				SpeedDisplayMode.Surface => telemetry.HorizonDown,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			AutopilotMode.RadialOut => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementRadialOut,
				SpeedDisplayMode.Target => telemetry.HorizonUp,
				SpeedDisplayMode.Surface => telemetry.HorizonUp,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			AutopilotMode.Normal => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementNormal,
				SpeedDisplayMode.Target => telemetry.HorizonNorth,
				SpeedDisplayMode.Surface => telemetry.HorizonNorth,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			AutopilotMode.Antinormal => vessel.speedMode switch
			{
				SpeedDisplayMode.Orbit => telemetry.OrbitMovementAntiNormal,
				SpeedDisplayMode.Target => telemetry.HorizonSouth,
				SpeedDisplayMode.Surface => telemetry.HorizonSouth,
				_ => throw new NotImplementedException($"{autopilot.AutopilotMode} => {vessel.speedMode}")
			},
			// What are these?
			//AutopilotMode.Navigation =>
			//AutopilotMode.Autopilot =>
			_ => throw new NotImplementedException($"{autopilot.AutopilotMode}")
		};

		#endregion

		#region Compute rotation

		// Magic! Don't remove!
		// (it updates some internal state, without that the last .UpdateRotation call would be ingored by this code and ship would continue to rotate)
		vessel.ControlTransform.Position.coordinateSystem.ToLocalVector(vessel.ControlTransform.up);

		// "vessel.transform.coordinateSystem" is the non-warp coordinate system
		var vcs = vessel.transform.up.coordinateSystem;
		// "vessel.ControlTransform.up.coordinateSystem" is outdated after the first rotation of this mod during warp
		var ccs = vessel.ControlTransform.coordinateSystem;

		// First find the rotation that would align the control part with the target direction
		var ctrl_rot = Rotation.FromTo(ccs.up, targ_forward).Normalized();
		// And apply it to vessel looking directions
		var new_forward = new Vector(vcs, (vcs.FixedToLocalRotation(ctrl_rot) * vcs.forward.vector).normalized);
		var new_up = new Vector(vcs, (vcs.FixedToLocalRotation(ctrl_rot) * vcs.up.vector).normalized);

		// Then use changed looking directions to create a new look rotation
		// Very important to normalize here!
		// Otherwise the camera starts floating away
		var final_rot = Rotation.LookRotation(new_forward, new_up).Normalized();

		// This overrides the rotation, rather than adding to the current one
		vessel.transform.UpdateRotation(final_rot);

		#endregion

		#region Punch the game to apply new rotation

		// "HandleOrbitalPhysicsUnderThrustStart" should not be called without any thrust
		// (otherwise it sets internal thrust to 100%, but uses no fuel)
		//if (vessel.flightCtrlState.mainThrottle==0) return;
		if (!vessel.IsUnderEngineThrust()) return;
		//if (!vessel.IsOrbitalPhysicsUnderThrustActive) return;
		// Recalculate the thrust-on-rails trajectory using new vessel rotation
		//var sw = System.Diagnostics.Stopwatch.StartNew();
		vessel.GetType().GetMethod("HandleOrbitalPhysicsUnderThrustStart", AllBF).Invoke(vessel, null);
		//Logger.LogDebug($"Recalculated thrust-warp trajectory in {sw.Elapsed}");

		if (vessel.IsOrbitalPhysicsUnderThrustActive) return;
		Logger.LogError("This shouldn't happen, I disabled the check that turns off thrust-on-rails");
		// If not, there was an error in KSP code (look in Alt+C),
		// at least cancel thrust so it doesn't burn propelant
		var vehicle = (VesselVehicle)GameManager.Instance.Game.ViewController.GetActiveVehicle(true);
		vehicle.AtomicSet(mainThrottle: 0, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

		// Hmmm, white box instead of proper message?
		GameManager.Instance.Game.Notifications.ProcessNotification(new NotificationData
		{
			Tier = NotificationTier.Alert,
			Primary = new NotificationLineItemData { LocKey = "SASedWarp/MathBug" }
		});

		#endregion

	}

	#endregion

}


