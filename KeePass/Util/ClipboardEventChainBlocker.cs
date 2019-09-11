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
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;

using KeePass.Native;

namespace KeePass.Util
{
	public sealed class ClipboardEventChainBlocker : IDisposable
	{
		private ClipboardBlockerForm m_form;
		private IntPtr m_hChain;

		public ClipboardEventChainBlocker()
		{
			m_form = new ClipboardBlockerForm();
			m_hChain = NativeMethods.SetClipboardViewer(m_form.Handle);
		}

		~ClipboardEventChainBlocker()
		{
			this.Dispose();
		}

		public void Dispose()
		{
			if(m_form != null)
			{
				if(NativeMethods.ChangeClipboardChain(m_form.Handle,
					m_hChain) == false)
				{
					Debug.Assert(false);
				}

				m_form.Dispose();
				m_form = null;
			}

			m_hChain = IntPtr.Zero;
		}

		private sealed class ClipboardBlockerForm : Form
		{
			public ClipboardBlockerForm() : base()
			{
				this.Visible = false;
				this.ShowInTaskbar = false;
				this.ShowIcon = false;
			}

			public override bool PreProcessMessage(ref Message msg)
			{
				if(msg.Msg == NativeMethods.WM_DRAWCLIPBOARD)
					return true; // Block message

				return base.PreProcessMessage(ref msg);
			}
		}
	}
}
