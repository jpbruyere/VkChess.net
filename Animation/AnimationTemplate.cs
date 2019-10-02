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
using MiscUtil;
using System.Reflection;

namespace vke
{
	public class Animation<T> : Animation
	{
		public delegate T GetterDelegate();
		public delegate void SetterDelegate(T value);

		public T TargetValue;
		T initialValue;
		T zero;
		public T Step;
		public bool Cycle;

		protected GetterDelegate getValue;
		protected SetterDelegate setValue;

		#region CTOR
		public Animation(Object instance, string _propertyName)
		{
			propertyName = _propertyName;
			AnimatedInstance = instance;
			PropertyInfo pi = instance.GetType().GetProperty(propertyName);
			getValue = (GetterDelegate)Delegate.CreateDelegate(typeof(GetterDelegate), instance, pi.GetGetMethod());
			setValue = (SetterDelegate)Delegate.CreateDelegate(typeof(SetterDelegate), instance, pi.GetSetMethod());
		}
		public Animation(Object instance, string _propertyName, T Target, T step)
		{
			propertyName = _propertyName;
			AnimatedInstance = instance;
			PropertyInfo pi = instance.GetType().GetProperty(propertyName);
			getValue = (GetterDelegate)Delegate.CreateDelegate(typeof(GetterDelegate), instance, pi.GetGetMethod());
			setValue = (SetterDelegate)Delegate.CreateDelegate(typeof(SetterDelegate), instance, pi.GetSetMethod());

			TargetValue = Target;

			T value = getValue();
			initialValue = value;
			Type t = typeof(T);

			if (t.IsPrimitive) {
				Step = (T)Convert.ChangeType (step, t);
				zero = (T)Convert.ChangeType (0, t);
			}else {
				Step = (T)Activator.CreateInstance (typeof(T), new Object[] { step });
				zero = (T)Activator.CreateInstance (typeof(T), 0f);
			}
			T test = (T)Operator.SubtractAlternative (value, TargetValue);

			if (Operator.LessThan(test, zero))
			{
				if (Operator.LessThan (Step, zero))
					Step = Operator.Negate (Step);
			}
			else if (Operator.GreaterThan(Step, zero))
				Step = Operator.Negate (Step);
		}
		#endregion

		public override void Process()
		{
			T value = getValue();

			//Debug.WriteLine ("Anim: {0} <= {1}", value, this.ToString ());

			if (Operator.GreaterThan(Step, zero))
			{
				value = Operator.Add (value, Step);
				setValue(value);
				//Debug.WriteLine(value);
				if (Operator.GreaterThan(Operator.Subtract(TargetValue, value), zero))
					return;
			}
			else
			{
				value = Operator.Add (value, Step);
				setValue(value);

				if (Operator.LessThan(Operator.Subtract(TargetValue, value), zero))
					return;
			}

			if (Cycle) {
				Step = Operator.Negate (Step);
				TargetValue = initialValue;
				Cycle = false;
				return;
			}

			setValue(TargetValue);
			AnimationList.Remove(this);

			RaiseAnimationFinishedEvent ();
		}

		public override string ToString ()
		{
			return string.Format ("{0}:->{1}:{2}",base.ToString(),TargetValue,Step);
		}
	}

}

