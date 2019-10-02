//
//  BezierPath.cs
//
//  Author:
//       Jean-Philippe Bruyère <jp.bruyere@hotmail.com>
//
//  Copyright (c) 2016 jp
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Numerics;

namespace vke
{
	public class BezierPath : Path
	{
		public Vector3 ControlPointStart;
		public Vector3 ControlPointEnd;

		public BezierPath (Vector3 startPos, Vector3 controlPointStart,
			Vector3 controlPointEnd, Vector3 endPos)
			:base(startPos, endPos)
		{
			ControlPointStart = controlPointStart;
			ControlPointEnd = controlPointEnd;
		}
		public BezierPath (Vector3 startPos, Vector3 endPos, Vector3 vUp)
			:base(startPos, endPos)
		{
			ControlPointStart = startPos + vUp;
			ControlPointEnd = endPos + vUp;
		}
		public override Vector3 GetStep (float pos)
		{
			return Path.CalculateBezierPoint (pos, Start, ControlPointStart, ControlPointEnd, End);
		}
	}
}

