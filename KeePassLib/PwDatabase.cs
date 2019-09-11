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
using System.Diagnostics;
using System.IO;

using KeePassLib.Collections;
using KeePassLib.Cryptography;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Delegates;
using KeePassLib.Interfaces;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeePassLib
{
	/// <summary>
	/// The core password manager class. It contains a number of groups, which
	/// contain the actual entries.
	/// </summary>
	public sealed class PwDatabase
	{
		// Initializations see Clear()
		private PwGroup m_pgRootGroup = null;
		private PwObjectList<PwDeletedObject> m_vDeletedObjects = new PwObjectList<PwDeletedObject>();

		private PwUuid m_uuidDataCipher = new PwUuid(StandardAesEngine.AesUuidBytes);
		private PwCompressionAlgorithm m_caCompression = PwCompressionAlgorithm.GZip;
		private ulong m_uKeyEncryptionRounds = PwDefs.DefaultKeyEncryptionRounds;

		private static bool m_bPrimaryCreated = false;

		private CompositeKey m_pwUserKey = null;
		private MemoryProtectionConfig m_memProtConfig = new MemoryProtectionConfig();

		private string m_strName = string.Empty;
		private string m_strDesc = string.Empty;
		private string m_strDefaultUserName = string.Empty;
		private uint m_uMntncHistoryDays = 365;

		private IOConnectionInfo m_ioSource = new IOConnectionInfo();
		private bool m_bDatabaseOpened = false;
		private bool m_bModified = false;

		private static string m_strLocalizedAppName = string.Empty;

		/// <summary>
		/// Get the root group that contains all groups and entries stored in the
		/// database.
		/// </summary>
		/// <returns>Root group. The return value is <c>null</c>, if no database
		/// has been opened.</returns>
		public PwGroup RootGroup
		{
			get { return m_pgRootGroup; }
			set
			{
				Debug.Assert(value != null);
				if(value == null) throw new ArgumentNullException("value");

				m_pgRootGroup = value;
			}
		}

		/// <summary>
		/// <c>IOConnection</c> of the currently opened database file.
		/// Must not be <c>null</c>.
		/// </summary>
		public IOConnectionInfo IOConnectionInfo
		{
			get { return m_ioSource; }
			set
			{
				Debug.Assert(value != null);
				if(value == null) throw new ArgumentNullException("value");

				m_ioSource = value;
			}
		}

		/// <summary>
		/// If this is <c>true</c>, a database is currently open.
		/// </summary>
		public bool IsOpen
		{
			get { return m_bDatabaseOpened; }
		}

		/// <summary>
		/// Modification flag. If true, the class has been modified and the
		/// user interface should prompt the user to save the changes before
		/// closing the database for example.
		/// </summary>
		public bool Modified
		{
			get { return m_bModified; }
			set { m_bModified = value; }
		}

		/// <summary>
		/// The user key used for database encryption. This key must be created
		/// and set before using any of the database load/save functions.
		/// </summary>
		public CompositeKey MasterKey
		{
			get { return m_pwUserKey; }
			set
			{
				Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();

				m_pwUserKey = value;
			}
		}

		/// <summary>
		/// Name of the database.
		/// </summary>
		public string Name
		{
			get { return m_strName; }
			set
			{
				Debug.Assert(value != null);
				if(value != null) m_strName = value;
			}
		}

		/// <summary>
		/// Database description.
		/// </summary>
		public string Description
		{
			get { return m_strDesc; }
			set
			{
				Debug.Assert(value != null);
				if(value != null) m_strDesc = value;
			}
		}

		/// <summary>
		/// Default user name used for new entries.
		/// </summary>
		public string DefaultUserName
		{
			get { return m_strDefaultUserName; }
			set
			{
				Debug.Assert(value != null);
				if(value != null) m_strDefaultUserName = value;
			}
		}

		/// <summary>
		/// Number of days until history entries are being deleted
		/// in a database maintenance operation.
		/// </summary>
		public uint MaintenanceHistoryDays
		{
			get { return m_uMntncHistoryDays; }
			set { m_uMntncHistoryDays = value; }
		}

		/// <summary>
		/// The encryption algorithm used to encrypt the data part of the database.
		/// </summary>
		public PwUuid DataCipherUuid
		{
			get { return m_uuidDataCipher; }
			set
			{
				Debug.Assert(value != null);
				if(value != null) m_uuidDataCipher = value;
			}
		}

		/// <summary>
		/// Compression algorithm used to encrypt the data part of the database.
		/// </summary>
		public PwCompressionAlgorithm Compression
		{
			get { return m_caCompression; }
			set { m_caCompression = value; }
		}

		/// <summary>
		/// Number of key transformation rounds (in order to make dictionary
		/// attacks harder).
		/// </summary>
		public ulong KeyEncryptionRounds
		{
			get { return m_uKeyEncryptionRounds; }
			set { m_uKeyEncryptionRounds = value; }
		}

		/// <summary>
		/// Memory protection configuration (for default fields).
		/// </summary>
		public MemoryProtectionConfig MemoryProtection
		{
			get { return m_memProtConfig; }
			set
			{
				Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();
				
				m_memProtConfig = value;
			}
		}

		/// <summary>
		/// Get a list of all deleted objects.
		/// </summary>
		public PwObjectList<PwDeletedObject> DeletedObjects
		{
			get { return m_vDeletedObjects; }
		}

		/// <summary>
		/// Localized application name.
		/// </summary>
		public static string LocalizedAppName
		{
			get { return m_strLocalizedAppName; }
			set { Debug.Assert(value != null); m_strLocalizedAppName = value; }
		}

		/// <summary>
		/// Constructs an empty password manager object.
		/// </summary>
		public PwDatabase()
		{
			if(m_bPrimaryCreated == false)
			{
				m_bPrimaryCreated = true;
			}

			Clear();
		}

		private void Clear()
		{
			m_pgRootGroup = null;
			m_vDeletedObjects = new PwObjectList<PwDeletedObject>();

			m_uuidDataCipher = new PwUuid(StandardAesEngine.AesUuidBytes);
			m_caCompression = PwCompressionAlgorithm.GZip;

			m_uKeyEncryptionRounds = PwDefs.DefaultKeyEncryptionRounds;

			m_pwUserKey = null;
			m_memProtConfig = new MemoryProtectionConfig();

			m_strName = string.Empty;
			m_strDesc = string.Empty;
			m_strDefaultUserName = string.Empty;

			m_ioSource = new IOConnectionInfo();
			m_bDatabaseOpened = false;
			m_bModified = false;
		}

		/// <summary>
		/// Initialize the class for managing a new database. Previously loaded
		/// data is deleted.
		/// </summary>
		/// <param name="ioConnection">IO connection of the new database.</param>
		/// <param name="pwKey">Key to open the database.</param>
		public void New(IOConnectionInfo ioConnection, CompositeKey pwKey)
		{
			Debug.Assert(ioConnection != null);
			if(ioConnection == null) throw new ArgumentNullException("ioConnection");
			Debug.Assert(pwKey != null);
			if(pwKey == null) throw new ArgumentNullException("pwKey");

			Close();

			m_ioSource = ioConnection;
			m_pwUserKey = pwKey;

			m_bDatabaseOpened = true;
			m_bModified = true;
			// m_bLocked = false;

			m_pgRootGroup = new PwGroup(true, true,
				UrlUtil.StripExtension(UrlUtil.GetFileName(ioConnection.Url)),
				PwIcon.FolderOpen);
			m_pgRootGroup.IsExpanded = true;
		}

		/// <summary>
		/// Open a database. The URL may point to any supported data source.
		/// </summary>
		/// <param name="ioSource">IO connection to load the database from.</param>
		/// <param name="pwKey">Key used to open the specified database.</param>
		/// <param name="slLogger">Logger, which gets all status messages.</param>
		public void Open(IOConnectionInfo ioSource, CompositeKey pwKey,
			IStatusLogger slLogger)
		{
			Debug.Assert(ioSource != null);
			if(ioSource == null) throw new ArgumentNullException("ioSource");
			Debug.Assert(pwKey != null);
			if(pwKey == null) throw new ArgumentNullException("pwKey");

			Close();

			try
			{
				m_pgRootGroup = new PwGroup(true, true, UrlUtil.StripExtension(
					UrlUtil.GetFileName(ioSource.Url)), PwIcon.FolderOpen);
				m_pgRootGroup.IsExpanded = true;

				m_pwUserKey = pwKey;

				m_bModified = false;

				Kdb4File kdb4 = new Kdb4File(this);
				Stream s = IOConnection.OpenRead(ioSource);
				kdb4.Load(s, Kdb4Format.Default, slLogger);
				s.Close();

				m_bDatabaseOpened = true;
				m_ioSource = ioSource;
			}
			catch(Exception ex)
			{
				this.Clear();
				throw ex;
			}
		}

		/// <summary>
		/// Save the currently opened database. The file is written to the location
		/// it has been opened from.
		/// </summary>
		/// <param name="slLogger">Logger that recieves status information.</param>
		public void Save(IStatusLogger slLogger)
		{
			Kdb4File kdb = new Kdb4File(this);

			Stream s = IOConnection.OpenWrite(m_ioSource);
			kdb.Save(s, Kdb4Format.Default, slLogger);

			m_bModified = false;
		}

		/// <summary>
		/// Save the currently opened database to a different location. If
		/// <paramref name="bIsPrimaryNow" /> is <c>true</c>, the specified
		/// location is made the default location for future saves
		/// using <c>SaveDatabase</c>.
		/// </summary>
		/// <param name="ioConnection">New location to serialize the database to.</param>
		/// <param name="bIsPrimaryNow">If <c>true</c>, the new location is made the
		/// standard location for the database. If <c>false</c>, a copy of the currently
		/// opened database is saved to the specified location, but it isn't
		/// made the default location (i.e. no lockfiles will be moved for
		/// example).</param>
		/// <param name="slLogger">Logger that recieves status information.</param>
		public void SaveAs(IOConnectionInfo ioConnection, bool bIsPrimaryNow,
			IStatusLogger slLogger)
		{
			Debug.Assert(ioConnection != null);
			if(ioConnection == null) throw new ArgumentNullException("ioConnection");

			IOConnectionInfo ioCurrent = m_ioSource; // Remember current
			m_ioSource = ioConnection;

			try { this.Save(slLogger); }
			catch(Exception ex)
			{
				m_ioSource = ioCurrent;
				throw ex;
			}

			if(!bIsPrimaryNow) m_ioSource = ioCurrent;
		}

		/// <summary>
		/// Closes the currently opened database. No confirmation message is shown
		/// before closing. Unsaved changes will be lost.
		/// </summary>
		public void Close()
		{
			Clear();
		}

		/// <summary>
		/// Synchronize the current database with another one.
		/// </summary>
		/// <param name="pwSource">Input database to synchronize with. This input
		/// database is used to update the current one, but is not modified! You
		/// must copy the current object if you want a second instance of the
		/// synchronized database. The input database must not be seen as valid
		/// database any more after calling <c>Synchronize</c>.</param>
		/// <param name="mm">Merge method.</param>
		public void MergeIn(PwDatabase pwSource, PwMergeMethod mm)
		{
			if(mm == PwMergeMethod.CreateNewUuids)
			{
				pwSource.RootGroup.CreateNewItemUuids(true, true, true);
			}

			GroupHandler gh = delegate(PwGroup pg)
			{
				if(pg == pwSource.m_pgRootGroup) return true;

				PwGroup pgLocal = m_pgRootGroup.FindGroup(pg.Uuid, true);
				if(pgLocal == null)
				{
					PwGroup pgSourceParent = pg.ParentGroup;
					PwGroup pgLocalContainer;
					if(pgSourceParent == pwSource.m_pgRootGroup)
						pgLocalContainer = m_pgRootGroup;
					else
						pgLocalContainer = m_pgRootGroup.FindGroup(pgSourceParent.Uuid, true);
					Debug.Assert(pgLocalContainer != null);

					PwGroup pgNew = new PwGroup();
					pgNew.Uuid = pg.Uuid;
					pgNew.ParentGroup = pgLocalContainer;
					pgNew.AssignProperties(pg, false);
					pgLocalContainer.Groups.Add(pgNew);
				}
				else // pgLocal != null
				{
					Debug.Assert(mm != PwMergeMethod.CreateNewUuids);

					if(mm == PwMergeMethod.OverwriteExisting)
						pgLocal.AssignProperties(pg, false);
					else if((mm == PwMergeMethod.OverwriteIfNewer) ||
						(mm == PwMergeMethod.Synchronize))
					{
						pgLocal.AssignProperties(pg, true);
					}
					// else if(mm == PwMergeMethod.KeepExisting) ...
				}

				return true;
			};

			EntryHandler eh = delegate(PwEntry pe)
			{
				PwEntry peLocal = m_pgRootGroup.FindEntry(pe.Uuid, true);
				if(peLocal == null)
				{
					PwGroup pgSourceParent = pe.ParentGroup;
					PwGroup pgLocalContainer;
					if(pgSourceParent == pwSource.m_pgRootGroup)
						pgLocalContainer = m_pgRootGroup;
					else
						pgLocalContainer = m_pgRootGroup.FindGroup(pgSourceParent.Uuid, true);
					Debug.Assert(pgLocalContainer != null);

					PwEntry peNew = new PwEntry(null, false, false);
					peNew.Uuid = pe.Uuid;
					peNew.ParentGroup = pgLocalContainer;
					peNew.AssignProperties(pe, false, true);
					pgLocalContainer.Entries.Add(peNew);
				}
				else // peLocal == null
				{
					Debug.Assert(mm != PwMergeMethod.CreateNewUuids);

					if(mm == PwMergeMethod.OverwriteExisting)
						peLocal.AssignProperties(pe, false, true);
					else if((mm == PwMergeMethod.OverwriteIfNewer) ||
						(mm == PwMergeMethod.Synchronize))
					{
						peLocal.AssignProperties(pe, true, true);
					}
					// else if(mm == PwMergeMethod.KeepExisting) ...
				}

				return true;
			};

			if(!pwSource.RootGroup.TraverseTree(TraversalMethod.PreOrder, gh, eh))
				throw new InvalidOperationException();

			if(mm == PwMergeMethod.Synchronize)
			{
				ApplyDeletions(pwSource.m_vDeletedObjects);
				ApplyDeletions(m_vDeletedObjects);
			}
		}

		/// <summary>
		/// Apply a list of deleted objects.
		/// </summary>
		/// <param name="listDelObjects">List of deleted objects.</param>
		private void ApplyDeletions(PwObjectList<PwDeletedObject> listDelObjects)
		{
			Debug.Assert(listDelObjects != null); if(listDelObjects == null) throw new ArgumentNullException();

			LinkedList<PwGroup> listGroupsToDelete = new LinkedList<PwGroup>();
			LinkedList<PwEntry> listEntriesToDelete = new LinkedList<PwEntry>();

			GroupHandler gh = delegate(PwGroup pg)
			{
				if(pg == m_pgRootGroup) return true;

				foreach(PwDeletedObject pdo in listDelObjects)
				{
					if(pg.Uuid.EqualsValue(pdo.Uuid))
						if(pg.LastModificationTime < pdo.DeletionTime)
							listGroupsToDelete.AddLast(pg);
				}

				return true;
			};

			EntryHandler eh = delegate(PwEntry pe)
			{
				foreach(PwDeletedObject pdo in listDelObjects)
				{
					if(pe.Uuid.EqualsValue(pdo.Uuid))
						if(pe.LastModificationTime < pdo.DeletionTime)
							listEntriesToDelete.AddLast(pe);
				}

				return true;
			};

			m_pgRootGroup.TraverseTree(TraversalMethod.PreOrder, gh, eh);

			foreach(PwGroup pg in listGroupsToDelete)
				pg.ParentGroup.Groups.Remove(pg);
			foreach(PwEntry pe in listEntriesToDelete)
				pe.ParentGroup.Entries.Remove(pe);
		}

		/// <summary>
		/// Synchronize current database with another one.
		/// </summary>
		/// <param name="strFile">Source file.</param>
		public void Synchronize(string strFile)
		{
			PwDatabase pwSource = new PwDatabase();

			IOConnectionInfo ioc = IOConnectionInfo.FromPath(strFile);
			pwSource.Open(ioc, this.m_pwUserKey, null);

			MergeIn(pwSource, PwMergeMethod.Synchronize);
		}
	}
}
