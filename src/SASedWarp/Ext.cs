namespace SASedWarp;



internal static class Ext
{

	public static KSP.Sim.Rotation Normalized(this KSP.Sim.Rotation rot)
	{
		var lrot = rot.localRotation;
		lrot.Normalize();
		return new KSP.Sim.Rotation(rot.coordinateSystem, lrot);
	}

	public static QuaternionD FixedToLocalRotation(this KSP.Api.ICoordinateSystem new_cs, KSP.Sim.Rotation r)
	{
		var new_xyz = new_cs.ToLocalVector(new KSP.Sim.Vector(r.coordinateSystem, r.localRotation.xyz));
		return new QuaternionD(new_xyz, r.localRotation.w);
	}

}


