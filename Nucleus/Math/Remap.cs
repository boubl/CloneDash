﻿namespace Nucleus
{
	public static partial class NMath
	{
		/// <summary>
		/// Remapping function. Given an <paramref name="input"/>, converts that input from the input range <paramref name="inStart"/> -> <paramref name="inEnd"/> into a range from <paramref name="outStart"/> -> <paramref name="outEnd"/>.
		/// </summary>
		/// <param name="input">The input value</param>
		/// <param name="inStart">The start of the input range</param>
		/// <param name="inEnd">The end of the input range</param>
		/// <param name="outStart">The start of the output range</param>
		/// <param name="outEnd">The end of the output range</param>
		/// <param name="clampInput">Should the input be clamped to fit within <paramref name="inStart"/> -> <paramref name="inEnd"/></param>
		/// <param name="clampOutput">Should the input be clamped to fit within <paramref name="outStart"/> -> <paramref name="outEnd"/></param>
		/// <returns><paramref name="input"/> remapped to be between <paramref name="outStart"/> -> <paramref name="outEnd"/></returns>
		public static double Remap(double input, double inStart, double inEnd, double outStart, double outEnd, bool clampInput = false, bool clampOutput = false) {
			if (clampInput)
				input = Math.Clamp(input, inStart, inEnd);

			var ret = outStart + (input - inStart) * (outEnd - outStart) / (inEnd - inStart);

			if (clampOutput)
				if (outEnd < outStart)
					ret = Math.Clamp(ret, outEnd, outStart);
				else
					ret = Math.Clamp(ret, outStart, outEnd);

			return ret;
		}
	}
}
