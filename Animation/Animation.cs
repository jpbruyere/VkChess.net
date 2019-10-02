//
//  Animation.cs
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
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;

namespace vke
{
	public delegate void AnimationEventHandler(Animation a);

    public delegate float GetterDelegate();
    public delegate void SetterDelegate(float value);

    public class Animation
    {
		public event AnimationEventHandler AnimationFinished;

		public static Random random = new Random ();
		public static int DelayMs = 0;

        protected GetterDelegate getValue;
        protected SetterDelegate setValue;

        public string propertyName;

        protected Stopwatch timer = new Stopwatch();
        protected int delayStartMs = 0;
		/// <summary>
		/// Delay before firing ZnimationFinished event.
		/// </summary>
		protected int delayFinishMs = 0;
        public static List<Animation> AnimationList = new List<Animation>();
		public static bool HasAnimations => AnimationList.Count > 0;
        //public FieldInfo member;
        public Object AnimatedInstance;

		#region CTOR
		public Animation (){}
		public Animation(Object instance, string _propertyName)
		{
			propertyName = _propertyName;
			AnimatedInstance = instance;
			PropertyInfo pi = instance.GetType().GetProperty(propertyName);
			try {
				getValue = (GetterDelegate)Delegate.CreateDelegate(typeof(GetterDelegate), instance, pi.GetGetMethod());
				setValue = (SetterDelegate)Delegate.CreateDelegate(typeof(SetterDelegate), instance, pi.GetSetMethod());
			} catch (Exception ex) {
				Debug.WriteLine (ex.ToString ());
			}
		}
		#endregion

		public static void StartAnimation(Animation a, int delayMs = 0, AnimationEventHandler OnEnd = null)
        {
			lock (AnimationList) {
				Animation aa = null;
				if (Animation.GetAnimation (a.AnimatedInstance, a.propertyName, ref aa)) {
					aa.CancelAnimation ();
				}

				//a.AnimationFinished += onAnimationFinished;

				a.AnimationFinished += OnEnd;
				a.delayStartMs = delayMs + DelayMs;


				if (a.delayStartMs > 0)
					a.timer.Start ();
            
				AnimationList.Add (a);
			}

        }

        static Stack<Animation> anims = new Stack<Animation>();
		static int frame = 0;
        public static void ProcessAnimations()
        {
			frame++;

//			#region FLYING anim
//			if (frame % 20 == 0){
//				foreach (Player p in MagicEngine.CurrentEngine.Players) {
//					foreach (CardInstance c in p.InPlay.Cards.Where(ci => ci.HasAbility(AbilityEnum.Flying) && ci.z < 0.4f)) {
//						
//					}
//				}
//			}
//			#endregion
            //Stopwatch animationTime = new Stopwatch();
            //animationTime.Start();
			 
			const int maxAnim = 200000;
			int count = 0;


			lock (AnimationList) {
				if (anims.Count == 0)
					anims = new Stack<Animation> (AnimationList);
			}
        
			while (anims.Count > 0 && count < maxAnim) {
				Animation a = anims.Pop ();	
				if (a == null)
					continue;
				if (a.timer.IsRunning) {
					if (a.timer.ElapsedMilliseconds > a.delayStartMs)
						a.timer.Stop ();
					else
						continue;
				}

				a.Process ();
				count++;
			}
				
            //animationTime.Stop();
            //Debug.WriteLine("animation: {0} ticks \t {1} ms ", animationTime.ElapsedTicks,animationTime.ElapsedMilliseconds);
        }
        public static bool GetAnimation(object instance, string PropertyName, ref Animation a)
        {
			for (int i = 0; i < AnimationList.Count; i++) {
				Animation anim = AnimationList [i];
				if (anim == null) {					
					continue;
				}
				if (anim.AnimatedInstance == instance && anim.propertyName == PropertyName) {
					a = anim;
					return true;
				}
			}

            return false;
        }
		public virtual void Process () {}
        public void CancelAnimation()
        {
			//Debug.WriteLine("Cancel anim: " + this.ToString()); 
            AnimationList.Remove(this);
        }
		public void RaiseAnimationFinishedEvent()
		{
			if (AnimationFinished != null)
				AnimationFinished (this);
		}

		public static void onAnimationFinished(Animation a)
		{
			Debug.WriteLine ("\t\tAnimation finished: " + a.ToString ());
		}


    }
}
