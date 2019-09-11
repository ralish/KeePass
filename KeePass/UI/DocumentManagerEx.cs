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
using System.Windows.Forms;
using System.Diagnostics;

using KeePassLib;
using KeePassLib.Serialization;

namespace KeePass.UI
{
	public sealed class DocumentManagerEx
	{
		private List<DocumentStateEx> m_vDocs = new List<DocumentStateEx>();
		private DocumentStateEx m_dsActive = null;

		public DocumentManagerEx()
		{
			m_dsActive = new DocumentStateEx();
			m_vDocs.Add(m_dsActive);
		}

		public DocumentStateEx ActiveDocument
		{
			get { return m_dsActive; }
			set
			{
				Debug.Assert(value != null); if(value == null) throw new ArgumentNullException("value");

				for(int i = 0; i < m_vDocs.Count; ++i)
				{
					if(m_vDocs[i] == value)
					{
						m_dsActive = value;
						return;
					}
				}

				throw new ArgumentException();
			}
		}

		public PwDatabase ActiveDatabase
		{
			get { return m_dsActive.Database; }
		}

		public uint DocumentCount
		{
			get { return (uint)m_vDocs.Count; }
		}

		public List<DocumentStateEx> Documents
		{
			get { return m_vDocs; }
		}

		public DocumentStateEx CreateNewDocument(bool bMakeActive)
		{
			DocumentStateEx ds = new DocumentStateEx();

			if((m_vDocs.Count == 1) && (!m_vDocs[0].Database.IsOpen) &&
				(m_vDocs[0].LockedIoc.Path.Length == 0))
			{
				m_vDocs.RemoveAt(0);
				m_dsActive = ds;
			}

			m_vDocs.Add(ds);
			if(bMakeActive) m_dsActive = ds;

			return ds;
		}

		public void CloseDatabase(PwDatabase pwDatabase)
		{
			int iFoundPos = -1;
			for(int i = 0; i < m_vDocs.Count; ++i)
			{
				if(m_vDocs[i].Database == pwDatabase)
				{
					iFoundPos = i;
					m_vDocs.RemoveAt(i);
					break;
				}
			}

			if(iFoundPos != -1)
			{
				if(m_vDocs.Count == 0)
					m_vDocs.Add(new DocumentStateEx());

				if(iFoundPos == m_vDocs.Count) --iFoundPos;
				m_dsActive = m_vDocs[iFoundPos];
			}
			else { Debug.Assert(false); }
		}

		public List<PwDatabase> GetOpenDatabases()
		{
			List<PwDatabase> list = new List<PwDatabase>();

			foreach(DocumentStateEx ds in m_vDocs)
				if(ds.Database.IsOpen)
					list.Add(ds.Database);

			return list;
		}
	}

	public sealed class DocumentStateEx
	{
		private PwDatabase m_pwDb = new PwDatabase();
		private IOConnectionInfo m_ioLockedIoc = new IOConnectionInfo();

		public PwDatabase Database
		{
			get { return m_pwDb; }
		}

		public IOConnectionInfo LockedIoc
		{
			get { return m_ioLockedIoc; }
			set
			{
				Debug.Assert(value != null); if(value == null) throw new ArgumentNullException("value");
				m_ioLockedIoc = value;
			}
		}
	}
}
