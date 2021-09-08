using System.Numerics;

namespace vkChess
{
	public static class ExtensionMethods {
		public static Vector3 ParseVec3 (string str) {
			if (string.IsNullOrEmpty (str))
				return new Vector3();
			string[] components = str.Substring(1, str.Length - 2).Split (',');
			if (components.Length != 3)
				throw new System.Exception ("Vector3 Parse Error, expecting 3 components.");
			return new Vector3(
				float.Parse (components[0]),
				float.Parse (components[1]),
				float.Parse (components[2])
			);
		}
	}
}