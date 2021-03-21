/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2020 Dominik Reichl <dominik.reichl@t-online.de>

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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.Resources;
using KeePass.UI;

using KeePassLib.Cryptography;
using KeePassLib.Cryptography.PasswordGenerator;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class EntropyForm : Form
	{
		private byte[] m_pbEntropy = null;
		private LinkedList<uint> m_llPool = new LinkedList<uint>();

		private Bitmap m_bmpRandom = null;

		public byte[] GeneratedEntropy
		{
			get { return m_pbEntropy; }
		}

		public static byte[] CollectEntropyIfEnabled(PwProfile pp)
		{
			if(!pp.CollectUserEntropy) return null;

			EntropyForm ef = new EntropyForm();
			if(UIUtil.ShowDialogNotValue(ef, DialogResult.OK)) return null;

			byte[] pbGen = ef.GeneratedEntropy;
			UIUtil.DestroyForm(ef);
			return pbGen;
		}

		public EntropyForm()
		{
			InitializeComponent();
			Program.Translation.ApplyTo(this);
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			// Can be invoked by tray command; don't use CenterParent
			Debug.Assert(this.StartPosition == FormStartPosition.CenterScreen);

			GlobalWindowManager.AddWindow(this);

			BannerFactory.CreateBannerEx(this, m_bannerImage,
				Properties.Resources.B48x48_Binary, KPRes.EntropyTitle,
				KPRes.EntropyDesc);
			this.Icon = AppIcons.Default;
			this.Text = KPRes.EntropyTitle;

			m_bmpRandom = CreateRandomBitmap(m_picRandom.ClientSize);
			m_picRandom.Image = m_bmpRandom;

			UpdateUIState();
			UIUtil.SetFocus(m_tbEdit, this);
		}

		private void UpdateUIState()
		{
			int nBits = m_llPool.Count / 8;
			Debug.Assert(!m_lblStatus.AutoSize); // For RTL support
			m_lblStatus.Text = KPRes.BitsEx.Replace(@"{PARAM}", nBits.ToString());

			if(nBits > 256) { Debug.Assert(false); m_pbGenerated.Value = 100; }
			else m_pbGenerated.Value = (nBits * 100) / 256;
		}

		private void OnRandomMouseMove(object sender, MouseEventArgs e)
		{
			if(m_llPool.Count >= 2048) return;

			uint ul = (uint)((e.X << 8) ^ e.Y);
			ul ^= (uint)(Environment.TickCount << 16);

			m_llPool.AddLast(ul);

			UpdateUIState();
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			using(MemoryStream ms = new MemoryStream())
			{
				// Prevent empty / low entropy buffer
				byte[] pbGuid = Guid.NewGuid().ToByteArray();
				ms.Write(pbGuid, 0, pbGuid.Length);

				foreach(uint ln in m_llPool)
					ms.Write(MemUtil.UInt32ToBytes(ln), 0, 4);

				if(m_tbEdit.Text.Length > 0)
				{
					byte[] pbUTF8 = StrUtil.Utf8.GetBytes(m_tbEdit.Text);
					ms.Write(pbUTF8, 0, pbUTF8.Length);
				}

				byte[] pbColl = ms.ToArray();

				m_pbEntropy = CryptoUtil.HashSha256(pbColl);

				CryptoRandom.Instance.AddEntropy(pbColl);
			}
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			if(m_bmpRandom != null)
			{
				m_picRandom.Image = null;

				m_bmpRandom.Dispose();
				m_bmpRandom = null;
			}
			else { Debug.Assert(false); }

			GlobalWindowManager.RemoveWindow(this);
		}

		private static Bitmap CreateRandomBitmap(Size sz)
		{
			int w = sz.Width, h = sz.Height;
			if((w <= 0) || (h <= 0)) { Debug.Assert(false); return null; }

			byte[] pbRandom = new byte[w * h];
			Program.GlobalRandom.NextBytes(pbRandom);

			Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);

			Rectangle rect = new Rectangle(0, 0, w, h);
			BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly,
				PixelFormat.Format32bppArgb);

			bool bFastCopy = (bd.Stride == (w * 4));
			Debug.Assert(bFastCopy); // 32 bits per pixel => no excess in line

			if(bFastCopy)
			{
				byte[] pbBmpData = new byte[w * h * 4];
				int p = 0;
				if(BitConverter.IsLittleEndian)
				{
					for(int i = 0; i < pbBmpData.Length; i += 4)
					{
						byte bt = pbRandom[p++];

						pbBmpData[i] = bt;
						pbBmpData[i + 1] = bt;
						pbBmpData[i + 2] = bt;
						pbBmpData[i + 3] = 255;
					}
				}
				else // Big-endian
				{
					for(int i = 0; i < pbBmpData.Length; i += 4)
					{
						byte bt = pbRandom[p++];

						pbBmpData[i] = 255;
						pbBmpData[i + 1] = bt;
						pbBmpData[i + 2] = bt;
						pbBmpData[i + 3] = bt;
					}
				}
				Debug.Assert(p == (w * h));

				Marshal.Copy(pbBmpData, 0, bd.Scan0, pbBmpData.Length);
			}

			bmp.UnlockBits(bd);

			if(!bFastCopy)
			{
				int p = 0;
				for(int y = 0; y < h; ++y)
				{
					for(int x = 0; x < w; ++x)
					{
						int c = pbRandom[p++];
						bmp.SetPixel(x, y, Color.FromArgb(255, c, c, c));
					}
				}
			}

			return bmp;
		}
	}
}
