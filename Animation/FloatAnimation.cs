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

namespace vke
{
	public class FloatAnimation : Animation
	{

		public float TargetValue;
		float initialValue;
		public float Step;
		public bool Cycle;

		#region CTOR
		public FloatAnimation(Object instance, string _propertyName, float Target, float step = 0.2f)
			: base(instance, _propertyName)
		{

			TargetValue = Target;

			float value = getValue();
			initialValue = value;

			Step = step;

			if (value < TargetValue)
			{
				if (Step < 0)
					Step = -Step;
			}
			else if (Step > 0)
				Step = -Step;            
		}
		#endregion

		public override void Process()
		{
			float value = getValue();

			//Debug.WriteLine ("Anim: {0} <= {1}", value, this.ToString ());

			if (Step > 0f)
			{
				value += Step;
				setValue(value);
				//Debug.WriteLine(value);
				if (TargetValue > value)
					return;
			}
			else
			{
				value += Step;
				setValue(value);

				if (TargetValue < value)
					return;
			}

			if (Cycle) {
				Step = -Step;
				TargetValue = initialValue;
				Cycle = false;
				return;
			}

			setValue(TargetValue);
			lock(AnimationList)
				AnimationList.Remove(this);

			RaiseAnimationFinishedEvent ();
		}

		public override string ToString ()
		{
			return string.Format ("{0}:->{1}:{2}",base.ToString(),TargetValue,Step);
		}
	}

}

