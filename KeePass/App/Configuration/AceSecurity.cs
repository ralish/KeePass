/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2008 Dominik Reichl <dominik.reichl@t-online.de>

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

namespace KeePass.App.Configuration
{
	public sealed class AceSecurity
	{
		public AceSecurity()
		{
		}

		private AceWorkspaceLocking m_wsl = new AceWorkspaceLocking();
		public AceWorkspaceLocking WorkspaceLocking
		{
			get { return m_wsl; }
			set
			{
				if(value == null) throw new ArgumentNullException("value");
				m_wsl = value;
			}
		}

		private AppPolicyFlags m_appPolicy = new AppPolicyFlags();
		public AppPolicyFlags Policy
		{
			get { return m_appPolicy; }
			set
			{
				if(value == null) throw new ArgumentNullException("value");
				m_appPolicy = value;
			}
		}

		private bool m_bClipClearOnExit = true;
		public bool ClipboardClearOnExit
		{
			get { return m_bClipClearOnExit; }
			set { m_bClipClearOnExit = value; }
		}

		private int m_nClipClearSeconds = 12;
		public int ClipboardClearAfterSeconds
		{
			get { return m_nClipClearSeconds; }
			set { m_nClipClearSeconds = value; }
		}
	}

	public sealed class AceWorkspaceLocking
	{
		public AceWorkspaceLocking()
		{
		}

		private bool m_bOnMinimize = false;
		public bool LockOnWindowMinimize
		{
			get { return m_bOnMinimize; }
			set { m_bOnMinimize = value; }
		}

		private bool m_bOnSessionLock = false;
		public bool LockOnSessionLock
		{
			get { return m_bOnSessionLock; }
			set { m_bOnSessionLock = value; }
		}

		private uint m_uLockAfterTime = 0;
		public uint LockAfterTime
		{
			get { return m_uLockAfterTime; }
			set { m_uLockAfterTime = value; }
		}
	}
}
