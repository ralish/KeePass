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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.IO;

using KeePass.UI;
using KeePass.Resources;

using KeePassLib.Cryptography;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class EntropyForm : Form
	{
		private byte[] m_pbEntropy = null;
		LinkedList<uint> m_llPool = new LinkedList<uint>();

		public byte[] GeneratedEntropy
		{
			get { return m_pbEntropy; }
		}

		public static byte[] CollectEntropyIfEnabled(PasswordGenerationOptions opt)
		{
			if(opt.CollectUserEntropy == false) return null;

			EntropyForm ef = new EntropyForm();
			if(ef.ShowDialog() == DialogResult.OK)
				return ef.GeneratedEntropy;

			return null;
		}

		public EntropyForm()
		{
			InitializeComponent();
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, BannerFactory.BannerStyle.Default,
				Properties.Resources.B48x48_Binary, KPRes.EntropyTitle,
				KPRes.EntropyDesc);
			this.Icon = Properties.Resources.KeePass;
			this.Text = KPRes.EntropyTitle;
		}

		private void OnRandomMouseMove(object sender, MouseEventArgs e)
		{
			if(m_llPool.Count >= 4096) return;

			uint ul = (uint)((e.X << 8) ^ e.Y);
			ul ^= (uint)(Environment.TickCount << 16);

			m_llPool.AddLast(ul);

			int nBits = m_llPool.Count / 8;
			m_lblStatus.Text = nBits.ToString() + " " + KPRes.Bits;

			if(nBits >= 256) nBits = 100;
			else nBits = (nBits * 100) / 256;
			m_pbGenerated.Value = nBits;
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			MemoryStream ms = new MemoryStream();

			foreach(uint ln in m_llPool)
				ms.Write(MemUtil.UInt32ToBytes(ln), 0, 4);

			if(m_tbEdit.Text.Length > 0)
			{
				UTF8Encoding utf8 = new UTF8Encoding();
				byte[] pbUTF8 = utf8.GetBytes(m_tbEdit.Text);
				ms.Write(pbUTF8, 0, pbUTF8.Length);
			}

			SHA256Managed sha256 = new SHA256Managed();
			m_pbEntropy = sha256.ComputeHash(ms.ToArray());
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
		}
	}
}