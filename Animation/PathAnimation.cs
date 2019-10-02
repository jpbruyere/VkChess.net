//
//  FloatAnimation.cs
//
//  Author:
//       Jean-Philippe Bruyère <jp.bruyere@hotmail.com>
//
//  Copyright (c) 2015 jp
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
	public class PathAnimation : Animation<Vector3>
	{		
		Path path;
		int stepCount;
		int currentStep;

		#region CTOR
		public PathAnimation(Object instance, string _propertyName, Path _path, int _stepCount = 20)
			: base(instance, _propertyName)
		{
			path = _path;
			stepCount = _stepCount;
		}

		#endregion

		public override void Process()
		{
			currentStep++;

			float t = (float)currentStep / (float)stepCount;
			Vector3 pos = path.GetStep (t);
			setValue(pos);

			if (currentStep < stepCount)
				return;
			
			AnimationList.Remove(this);
			RaiseAnimationFinishedEvent ();
		}

		public override string ToString ()
		{
			return string.Format ("{0}:->{1}:{2}",base.ToString(),TargetValue,Step);
		}
	}
}

