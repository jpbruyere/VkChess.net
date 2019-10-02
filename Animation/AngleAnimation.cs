//
//  AngleAnimation.cs
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
	public class AngleAnimation : FloatAnimation
	{
		#region CTOR
		public AngleAnimation(Object instance, string PropertyName, float Target, float step = 0.1f) : 
			base(instance,PropertyName,Target,step){}
		#endregion

		public override void Process()
		{
			base.Process();

			float value = getValue();
			if (value < -MathHelper.TwoPi)
				setValue(value + MathHelper.TwoPi);
			else if (value >= MathHelper.TwoPi)
				setValue(value - MathHelper.TwoPi);
		}
	}
}

