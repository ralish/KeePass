/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2007 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;

namespace KeePassLib.Utility
{
	/// <summary>
	/// Contains static buffer manipulation and string conversion routines.
	/// </summary>
	public static class MemUtil
	{
		/// <summary>
		/// Convert a hexadecimal string to a byte array. The input string must be
		/// even (i.e. its length is a multiple of 2).
		/// </summary>
		/// <param name="strHexString">String containing hexadecimal characters.</param>
		/// <returns>Returns a byte array. Returns <c>null</c> if the string parameter
		/// was <c>null</c> or is an uneven string (i.e. if its length isn't a
		/// multiple of 2).</returns>
		/// <exception cref="System.ArgumentNullException">Thrown if <paramref name="strHexString" />
		/// is <c>null</c>.</exception>
		public static byte[] HexStringToByteArray(string strHexString)
		{
			Debug.Assert(strHexString != null); if(strHexString == null) throw new ArgumentNullException();

			int nStrLen = strHexString.Length;
			if((nStrLen & 1) != 0) return null; // Only even strings supported

			byte[] pb = new byte[nStrLen / 2];
			byte bt;
			char ch;

			for(int i = 0; i < nStrLen; i++)
			{
				ch = strHexString[i];
				if((ch == ' ') || (ch == '\t') || (ch == '\r') || (ch == '\n')) continue;

				if((ch >= '0') && (ch <= '9'))
					bt = (byte)(ch - '0');
				else if((ch >= 'a') && (ch <= 'f'))
					bt = (byte)(ch - 'a' + 10);
				else if((ch >= 'A') && (ch <= 'F'))
					bt = (byte)(ch - 'A' + 10);
				else bt = 0;

				bt <<= 4;
				i++;

				ch = strHexString[i];
				if((ch >= '0') && (ch <= '9'))
					bt += (byte)(ch - '0');
				else if((ch >= 'a') && (ch <= 'f'))
					bt += (byte)(ch - 'a' + 10);
				else if((ch >= 'A') && (ch <= 'F'))
					bt += (byte)(ch - 'A' + 10);

				pb[i / 2] = bt;
			}

			return pb;
		}

		/// <summary>
		/// Convert a byte array to a hexadecimal string.
		/// </summary>
		/// <param name="pbArray">Input byte array.</param>
		/// <returns>Returns the hexadecimal string representing the byte
		/// array. Returns <c>null</c> if the input byte array was <c>null</c>. Returns
		/// an empty string ("") if the input byte array has length 0.</returns>
		public static string ByteArrayToHexString(byte[] pbArray)
		{
			StringBuilder sb = new StringBuilder();

			if(pbArray == null) return null;

			int nLen = pbArray.Length;
			if(nLen == 0) return "";

			byte bt, btHigh, btLow;
			for(int i = 0; i < nLen; i++)
			{
				bt = pbArray[i];
				btHigh = bt; btHigh >>= 4;
				btLow = (byte)(bt & 0x0F);

				if(btHigh >= 10) sb.Append((char)('A' + btHigh - 10));
				else sb.Append((char)('0' + btHigh));

				if(btLow >= 10) sb.Append((char)('A' + btLow - 10));
				else sb.Append((char)('0' + btLow));
			}

			return sb.ToString();
		}

		/// <summary>
		/// Set all bytes in a byte array to zero.
		/// </summary>
		/// <param name="pbArray">Input array. All bytes of this array will be set
		/// to zero.</param>
		public static void ZeroByteArray(byte[] pbArray)
		{
			Debug.Assert(pbArray != null); if(pbArray == null) throw new ArgumentNullException();

			// for(int i = 0; i < pbArray.Length; i++)
			//	pbArray[i] = 0;

			Array.Clear(pbArray, 0, pbArray.Length);
		}

		/// <summary>
		/// Convert 2 bytes to a 16-bit unsigned integer using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="pb">Input bytes. Array must contain at least 2 bytes.</param>
		/// <returns>16-bit unsigned integer.</returns>
		public static ushort BytesToUInt16(byte[] pb)
		{
			Debug.Assert((pb != null) && (pb.Length == 2));
			if(pb == null) throw new ArgumentNullException();
			if(pb.Length != 2) throw new ArgumentException();

			return (ushort)((ushort)pb[0] | ((ushort)pb[1] << 8));
		}

		/// <summary>
		/// Convert 4 bytes to a 32-bit unsigned integer using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="pb">Input bytes.</param>
		/// <returns>32-bit unsigned integer.</returns>
		public static uint BytesToUInt32(byte[] pb)
		{
			Debug.Assert((pb != null) && (pb.Length == 4));
			if(pb == null) throw new ArgumentNullException("pb");
			if(pb.Length != 4) throw new ArgumentException("Input array must contain 4 bytes!");

			return (uint)pb[0] | ((uint)pb[1] << 8) | ((uint)pb[2] << 16) |
				((uint)pb[3] << 24);
		}

		/// <summary>
		/// Convert 8 bytes to a 64-bit unsigned integer using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="pb">Input bytes.</param>
		/// <returns>64-bit unsigned integer.</returns>
		public static ulong BytesToUInt64(byte[] pb)
		{
			Debug.Assert((pb != null) && (pb.Length == 8));
			if(pb == null) throw new ArgumentNullException();
			if(pb.Length != 8) throw new ArgumentException();

			return (ulong)pb[0] | ((ulong)pb[1] << 8) | ((ulong)pb[2] << 16) |
				((ulong)pb[3] << 24) | ((ulong)pb[4] << 32) | ((ulong)pb[5] << 40) |
				((ulong)pb[6] << 48) | ((ulong)pb[7] << 56);
		}

		/// <summary>
		/// Convert a 16-bit unsigned integer to 2 bytes using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="uValue">16-bit input word.</param>
		/// <returns>Two bytes representing the 16-bit value.</returns>
		public static byte[] UInt16ToBytes(ushort uValue)
		{
			byte[] pb = new byte[2];

			pb[0] = (byte)(uValue & 0xFF);
			pb[1] = (byte)(uValue >> 8);

			return pb;
		}

		/// <summary>
		/// Convert a 32-bit unsigned integer to 4 bytes using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="uValue">32-bit input word.</param>
		/// <returns>Four bytes representing the 32-bit value.</returns>
		public static byte[] UInt32ToBytes(uint uValue)
		{
			byte[] pb = new byte[4];
			
			pb[0] = (byte)(uValue & 0xFF);
			pb[1] = (byte)((uValue >> 8) & 0xFF);
			pb[2] = (byte)((uValue >> 16) & 0xFF);
			pb[3] = (byte)((uValue >> 24) & 0xFF);
			
			return pb;
		}

		/// <summary>
		/// Convert a 64-bit unsigned integer to 8 bytes using Little-Endian
		/// encoding.
		/// </summary>
		/// <param name="uValue">64-bit input word.</param>
		/// <returns>Eight bytes representing the 64-bit value.</returns>
		public static byte[] UInt64ToBytes(ulong uValue)
		{
			byte[] pb = new byte[8];

			pb[0] = (byte)(uValue & 0xFF);
			pb[1] = (byte)((uValue >> 8) & 0xFF);
			pb[2] = (byte)((uValue >> 16) & 0xFF);
			pb[3] = (byte)((uValue >> 24) & 0xFF);
			pb[4] = (byte)((uValue >> 32) & 0xFF);
			pb[5] = (byte)((uValue >> 40) & 0xFF);
			pb[6] = (byte)((uValue >> 48) & 0xFF);
			pb[7] = (byte)((uValue >> 56) & 0xFF);

			return pb;
		}
	}
}
