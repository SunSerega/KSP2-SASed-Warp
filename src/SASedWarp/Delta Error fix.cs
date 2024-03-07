namespace SASedWarp;



using System;

using HarmonyLib;

using System.Linq;
using System.Collections.Generic;

using System.Reflection.Emit;



internal static class TranspilerHelper
{

	public sealed class ILLookupKey
	{
		public OpCode OpCode { get; set; }
		public object? Operand { get; set; } = null;
	}

	public static IEnumerable<CodeInstruction> Replace(
		IEnumerable<CodeInstruction> old_body,
		ILLookupKey[] lookup, Func<IReadOnlyCollection<CodeInstruction>, IEnumerable<CodeInstruction>> repl,
		Range expected_times
	)
	{
		var q = new Queue<CodeInstruction>(lookup.Length);
		var founc_c = 0;

		//var ind = -1;
		foreach (var instr in old_body)
		{
			//++ind;
			//VesselComponent_HandleOrbitalPhysicsUnderThrustStart_Patch.log.LogInfo($"[{ind}] {instr.opcode}: {instr.operand}");

			q.Enqueue(instr);
			if (q.Count < lookup.Length) continue;

			if (q.Zip(lookup, (instr, lookup) =>
			{
				if (instr.opcode != lookup.OpCode) return false;
				if (lookup.Operand is null) return true;
				if (!Equals(instr.operand, lookup.Operand)) return false;
				return true;
			}).All(b => b))
			{
				founc_c += 1;
				foreach (var mod_instr in repl(q))
					yield return mod_instr;
				q.Clear();
				continue;
			}

			yield return q.Dequeue();
		}
		foreach (var instr in q)
			yield return instr;
		if (founc_c<expected_times.Start.Value || founc_c>expected_times.End.Value)
			throw new InvalidOperationException($"Found expected IL {founc_c} times, instead of {expected_times}");
	}

}

[HarmonyPatch(typeof(KSP.Sim.impl.VesselComponent), "HandleOrbitalPhysicsUnderThrustStart", MethodType.Normal)]
public static class VesselComponent_HandleOrbitalPhysicsUnderThrustStart_Patch
{
	private static readonly SpaceWarp.API.Logging.ILogger log = SASedWarpPlugin.Instance.SWLogger;

	private static bool CheckNeedRoundingError(double pos_err, double vel_err)
	{
		var need_err = pos_err>=0.01 || vel_err>=0.01;
		if (need_err) log.LogDebug($"pos_err={pos_err}, vel_err={vel_err}");
		return false; // For now turned off completely
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> old_body)
	{
		var prop_get_sqrMagnitude = typeof(Vector3d).GetProperty("sqrMagnitude").GetGetMethod();
		var res = TranspilerHelper.Replace(old_body,
			[
					new() { OpCode = OpCodes.Ldloca_S },
					new() { OpCode = OpCodes.Call, Operand = prop_get_sqrMagnitude },
					new() { OpCode = OpCodes.Ldc_R8 },
					new() { OpCode = OpCodes.Bge_Un },
					new() { OpCode = OpCodes.Ldloca_S },
					new() { OpCode = OpCodes.Call, Operand = prop_get_sqrMagnitude },
					new() { OpCode = OpCodes.Ldc_R8 },
					new() { OpCode = OpCodes.Bge_Un },
			],
			old_body =>
			{
				Func<double, double, bool> check_need_rounding_error = CheckNeedRoundingError;
				var a = old_body.ToArray();
				return
				[
						a[0], a[1],
						a[4], a[5],
						new(OpCodes.Call, check_need_rounding_error.Method),
						new(OpCodes.Ldc_I4_1),
						a[7], new(OpCodes.Nop),
				];
			},
			1..1
		);//.ToArray();
		  //for (var ind=0; ind<res.Length; ind++)
		  //	log.LogInfo($"[{ind}] {res[ind].opcode}: {res[ind].operand}");
		return res;
	}

}


