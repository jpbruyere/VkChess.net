//
// Piece.cs
//
// Author:
//       Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// Copyright (c) 2019 
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using vke;

namespace vkChess {
    public class ChessMoves : List<String> {
        public void AddMove(string move) {
            if (!string.IsNullOrEmpty(move))
                base.Add(move);
        }
        public void AddMove(string[] moves) {
            if (moves == null)
                return;
            if (moves.Length > 0)
                base.AddRange(moves);
        }
    }

    [DebuggerDisplay("{DebuggerDisplay}")]
    public class Piece {
        static Vector4 blackColor = new Vector4(0.2f, 0.2f, 0.2f, 1f);
        static Vector4 whiteColor = new Vector4(1.0f, 1.0f, 1.0f, 1f);
        public static HostBuffer<VkChess.InstanceData> instanceBuff;
		public static VkChess.InstanceData[] boardDatas;
		public static uint flushStart, flushEnd;

        static uint nextInstanceIdx;
        float xAngle, zAngle;
        Vector3 position;
        public PieceType Type;
        public readonly ChessColor Player;
        public uint instanceIdx;

        public bool IsPromoted;
        public bool HasMoved;
        public bool Captured {
			get { return BoardCell < 0; }
			set {
				if (value)
					BoardCell = -1;
				else
					BoardCell = 0;
			} 
		}


		public int initX, initY;

        VkChess.InstanceData instData;

        public Piece (ChessColor player, PieceType type, int col, int line) {
            this.instanceIdx = nextInstanceIdx++;
            this.Type = type;
            this.Player = player;
            initX = col;
            initY = line;
            position.X = initX;
            position.Y = initY;
			BoardCell = new Crow.Point (col, line);

            instData = new VkChess.InstanceData(Player == ChessColor.White ? whiteColor : blackColor, Matrix4x4.Identity);

			if (player == ChessColor.Black && type == PieceType.Knight)
				zAngle = MathHelper.Pi;

			updatePos ();
        }

		public Crow.Point BoardCell;

        public Vector3 Position {
            get { return position; }
            set {
                if (value == position)
                    return;
                position = value;
                updatePos();
            }
        }

        public float X {
            get { return position.X; }
            set {
                if (position.X == value)
                    return;
                position.X = value;
                updatePos();
            }
        }
        public float Y {
            get { return position.Y; }
            set {
                if (position.Y == value)
                    return;
                position.Y = value;
                updatePos();
            }
        }
        public float Z {
            get { return position.Z; }
            set {
                if (position.Z == value)
                    return;
                position.Z = value;
                updatePos();
            }
        }
        public float XAngle {
            get { return xAngle; }
            set {
                if (xAngle == value)
                    return;
                xAngle = value;
                updatePos();
            }
        }
        public float ZAngle {
            get { return zAngle; }
            set {
                if (zAngle == value)
                    return;
                zAngle = value;
                updatePos();
            }
        }
        static Vector3 centerDiff = new Vector3(3.5f, 0, 3.5f);

        public void updatePos() {
            Quaternion q = Quaternion.CreateFromYawPitchRoll (zAngle, 0f, xAngle);
			Vector3 pos = (new Vector3 (position.X, position.Z, 7f-position.Y) - centerDiff) * 2;
            instData.mat = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(pos);
			UpdateBuff (instanceIdx, ref instData);
        }
		public static void UpdateBuff (uint instIdx, ref VkChess.InstanceData data) {
			if (instanceBuff == null)
				return;
			instanceBuff.Update (instIdx, data);

			if (flushEnd == 0) {
				flushStart = instIdx;
				flushEnd = instIdx + 1;
			} else if (instIdx < flushStart)
				flushStart = instIdx;
			else if (flushEnd <= instIdx)
				flushEnd = instIdx + 1;
		}
		public static void UpdateCase (int x, int y, Vector4 colorDiff) {
			int index = y * 8 + x;
			boardDatas[index].color += colorDiff;
			UpdateBuff ((uint)index+32, ref boardDatas[index]);
		}
		public static void FlushHostBuffer () {
            if (flushEnd == 0)
                return;
            instanceBuff.Flush(flushStart, flushEnd);
            flushEnd = flushStart = 0;
        }

        public void Reset(bool animate = true) {
            xAngle = 0f;
            Z = 0f;
            if (HasMoved) {
                if (animate)
                    Animation.StartAnimation(new PathAnimation(this, "Position",
                        new BezierPath(
                            Position,
                            new Vector3(initX, initY, 0f), Vector3.UnitZ)));
                //else
                //    Position = new Vector3(initX, initY, 0f);
            }
			if (IsPromoted)
            	Unpromote();
            IsPromoted = false;
            HasMoved = false;            
			BoardCell = new Crow.Point (initX, initY);
        }
		public void MoveTo (Crow.Point newPos, bool animate = false) {
			BoardCell = newPos;
			if (animate) {
				Animation.StartAnimation (new PathAnimation (this, "Position",
					new BezierPath (
					Position,
					new Vector3 (newPos.X, newPos.Y, 0f), Vector3.UnitZ), VkChess.animationSteps),
					0);
			} //else
			//	Position = new Vector3 (newPos.X, newPos.Y, 0f);
		}
		public void Promote(char prom, bool preview = false) {
            if (IsPromoted)
                throw new Exception("trying to promote already promoted " + Type.ToString());
            if (Type != PieceType.Pawn)
                throw new Exception("trying to promote " + Type.ToString());
            IsPromoted = true;
            switch (prom) {
                case 'q':
                    Type = PieceType.Queen;
                    break;
                case 'r':
                    Type = PieceType.Rook;
                    break;
                case 'b':
                    Type = PieceType.Bishop;
                    break;
                case 'k':
                    Type = PieceType.Knight;
                    break;
                default:
                    throw new Exception("Unrecognized promotion");
            }
			VkChess.updateInstanceCmds = true;
        }
        public void Unpromote() {
            IsPromoted = false;
            Type = PieceType.Pawn;
			VkChess.updateInstanceCmds = true;
		}

        string DebuggerDisplay => string.Format($"{Type}:{BoardCell.ToString()}");
    }
}
