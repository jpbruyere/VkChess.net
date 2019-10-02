//
//  ShakeAnimation.cs
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
	public class ShakeAnimation : Animation
	{
		const float stepMin = 0.001f, stepMax = 0.005f;
		bool rising = true;

		public float LowBound;
		public float HighBound;

		#region CTOR
		public ShakeAnimation(
			Object instance, 
			string _propertyName, 
			float lowBound, float highBound)
			: base(instance, _propertyName)
		{

			LowBound = Math.Min (lowBound, highBound);
			HighBound = Math.Max (lowBound, highBound);

			float value = getValue ();

			if (value > HighBound)
				rising = false;
		}
		#endregion

		public override void Process ()
		{
			float value = getValue ();	
			float step = stepMin + (float)random.NextDouble () * stepMax;

			if (rising) {				
				value += step;
				if (value > HighBound) {
					value = HighBound;
					rising = false;
				}
			} else {
				value -= step;
				if (value < LowBound) {
					value = LowBound;
					rising = true;
				} else if (value > HighBound)
					value -= step * 10f;
			}
			setValue (value);
		}

	}

}

