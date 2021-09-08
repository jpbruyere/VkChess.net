// Copyright (c) 2021-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.ComponentModel;
using System.Numerics;
using Crow;
using Crow.Drawing;
//using vkvg;

namespace vkChess
{
	public class Vector3Widget : TemplatedControl
	{
		Vector3 vector;
		protected float minValue, maxValue, smallStep, bigStep;
		protected int decimals;


		[DefaultValue(2)]
		public int Decimals
		{
			get => decimals;
			set
			{
				if (value == decimals)
					return;
				decimals = value;
				NotifyValueChangedAuto (decimals);
				RegisterForRedraw ();
			}
		}
		[DefaultValue(0.0f)]
		public virtual float Minimum {
			get => minValue;
			set {
				if (minValue == value)
					return;

				minValue = value;
				NotifyValueChangedAuto (minValue);
				RegisterForRedraw ();
			}
		}
		[DefaultValue(100.0f)]
		public virtual float Maximum
		{
			get => maxValue;
			set {
				if (maxValue == value)
					return;

				maxValue = value;
				NotifyValueChangedAuto (maxValue);
				RegisterForRedraw ();
			}
		}
		[DefaultValue(1.0f)]
		public virtual float SmallIncrement
		{
			get => smallStep;
			set {
				if (smallStep == value)
					return;

				smallStep = value;
				NotifyValueChangedAuto (smallStep);
				RegisterForRedraw ();
			}
		}
		[DefaultValue(5.0f)]
		public virtual float LargeIncrement
		{
			get => bigStep;
			set {
				if (bigStep == value)
					return;

				bigStep = value;
				NotifyValueChangedAuto (bigStep);
				RegisterForRedraw ();
			}
		}

		public Vector3 Value {
			get => vector;
			set {
				if (vector == value)
					return;
				vector = value;
				NotifyValueChangedAuto (vector);
				NotifyValueChanged ("X", vector.X);
				NotifyValueChanged ("Y", vector.Y);
				NotifyValueChanged ("Z", vector.Z);
			}
		}
		public float X {
			get => vector.X;
			set {
				if (X == value)
					return;
				vector.X = value;
				NotifyValueChangedAuto (vector.X);
				NotifyValueChanged ("Value", vector);
			}
		}
		public float Y {
			get => vector.Y;
			set {
				if (Y == value)
					return;
				vector.Y = value;
				NotifyValueChangedAuto (vector.Y);
				NotifyValueChanged ("Value", vector);
			}
		}
		public float Z {
			get => vector.Z;
			set {
				if (Z == value)
					return;
				vector.Z = value;
				NotifyValueChangedAuto (vector.Z);
				NotifyValueChanged ("Value", vector);
			}
		}
	}
}

