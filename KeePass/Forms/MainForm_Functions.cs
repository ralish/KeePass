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
using System.Text;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

using KeePass.App;
using KeePass.App.Configuration;
using KeePass.Resources;
using KeePass.DataExchange;
using KeePass.Native;
using KeePass.UI;
using KeePass.Util;
using KeePass.Plugins;

using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Delegates;
using KeePassLib.Interfaces;
using KeePassLib.Keys;
using KeePassLib.Utility;
using KeePassLib.Security;
using KeePassLib.Serialization;

using NativeLib = KeePassLib.Native.NativeLib;

namespace KeePass.Forms
{
	public partial class MainForm : Form
	{
		private DocumentManagerEx m_docMgr = new DocumentManagerEx();

		private ListViewGroup m_lvgLastEntryGroup = null;
		private bool m_bEntryGrouping = false;
		private DateTime m_dtCachedNow = DateTime.Now;
		private bool m_bOnlyTans = false;
		private Font m_fontExpired = new Font(FontFamily.GenericSansSerif, 8.25f);
		private Font m_fontBoldUI = null;
		private Point m_ptLastEntriesMouseClick = new Point(0, 0);
		private RichTextBoxContextMenu m_ctxEntryPreviewContextMenu = new RichTextBoxContextMenu();
		private DynamicMenu m_dynCustomStrings;
		private DynamicMenu m_dynCustomBinaries;

		private MemoryProtectionConfig m_viewHideFields = new MemoryProtectionConfig();

		private bool[] m_vShowColumns = new bool[(int)AppDefs.ColumnID.Count];
		private MruList m_mruList = new MruList();

		private SessionLockNotifier m_sessionLockNotifier = new SessionLockNotifier();

		private DefaultPluginHost m_pluginDefaultHost = new DefaultPluginHost();
		private PluginManager m_pluginManager = new PluginManager();

		private int m_nLockTimerMax = 0;
		private int m_nLockTimerCur = 0;
		private bool m_bAllowLockTimerMod = true;

		private int m_nClipClearMax = 0;
		private int m_nClipClearCur = -1;

		private string m_strNeverExpiresText = string.Empty;

		private bool m_bSimpleTanView = true;
		private bool m_bShowTanIndices = true;

		private Image m_imgFileSaveEnabled = null;
		private Image m_imgFileSaveDisabled = null;
		private Image m_imgFileSaveAllEnabled = null;
		private Image m_imgFileSaveAllDisabled = null;
		private ImageList m_ilCurrentIcons = null;

		private bool m_bIsAutoTyping = false;
		private bool m_bBlockTabChanged = false;

		private int m_nAppMessage = Program.ApplicationMessage;

		private List<KeyValuePair<ToolStripItem, ToolStripItem>> m_vLinkedToolStripItems =
			new List<KeyValuePair<ToolStripItem, ToolStripItem>>();

		private FormWindowState m_fwsLast = FormWindowState.Normal;

		public enum KeeExportFormat : int
		{
			PlainXml = 0,
			Html,
			Kdb3,
			UseXsl
		}

		internal DocumentManagerEx DocumentManager { get { return m_docMgr; } }
		public PwDatabase ActiveDatabase { get { return m_docMgr.ActiveDatabase; } }
		public ImageList ClientIcons { get { return m_ilCurrentIcons; } }

		/// <summary>
		/// Get a reference to the main menu.
		/// </summary>
		public MenuStrip MainMenu { get { return m_menuMain; } }
		/// <summary>
		/// Get a reference to the 'Tools' popup menu in the main menu. It is
		/// recommended that you use this reference instead of searching the
		/// main menu for the 'Tools' item.
		/// </summary>
		public ToolStripMenuItem ToolsMenu { get { return m_menuTools; } }

		/// <summary>
		/// Get a reference to the plugin host. This should not be used by plugins.
		/// Plugins should directly use the reference they get in the initialization
		/// function.
		/// </summary>
		public IPluginHost PluginHost
		{
			get { return m_pluginDefaultHost; }
		}

		/// <summary>
		/// Check if the main window is trayed (i.e. only the tray icon is visible).
		/// </summary>
		/// <returns>Returns <c>true</c>, if the window is trayed.</returns>
		public bool IsTrayed()
		{
			return !this.Visible;
		}

		public bool IsFileLocked(DocumentStateEx ds)
		{
			if(ds == null) ds = m_docMgr.ActiveDocument;

			return (ds.LockedIoc.Path.Length != 0);
		}

		public bool IsAtLeastOneFileOpen()
		{
			foreach(DocumentStateEx ds in m_docMgr.Documents)
				if(ds.Database.IsOpen) return true;

			return false;
		}

		private void CleanUpEx()
		{
			m_nClipClearCur = -1;
			if(Program.Config.Security.ClipboardClearOnExit)
				ClipboardUtil.ClearIfOwner();

			SaveConfig();

			m_sessionLockNotifier.Uninstall();
			HotKeyManager.UnregisterAll();
			m_pluginManager.UnloadAllPlugins();

			EntryTemplates.Clear();

			m_dynCustomBinaries.MenuClick -= this.OnEntryBinaryView;
			m_dynCustomStrings.MenuClick -= this.OnCopyCustomString;

			m_ctxEntryPreviewContextMenu.Detach();

			this.Visible = false;

			if(m_fontBoldUI != null) { m_fontBoldUI.Dispose(); m_fontBoldUI = null; }
		}

		/// <summary>
		/// Save the current configuration. The configuration is saved using the
		/// cascading configuration files mechanism and the default paths are used.
		/// </summary>
		public void SaveConfig()
		{
			SaveWindowPositionAndSize();

			AceMainWindow mw = Program.Config.MainWindow;

			mw.Layout = ((m_splitHorizontal.Orientation != Orientation.Horizontal) ?
				AceMainWindowLayout.SideBySide : AceMainWindowLayout.Default);

			mw.Columns[PwDefs.TitleField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Title].Width;
			mw.Columns[PwDefs.UserNameField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.UserName].Width;
			mw.Columns[PwDefs.PasswordField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Password].Width;
			mw.Columns[PwDefs.UrlField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Url].Width;
			mw.Columns[PwDefs.NotesField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Notes].Width;
			mw.Columns[AppDefs.ColumnIdnCreationTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.CreationTime].Width;
			mw.Columns[AppDefs.ColumnIdnLastAccessTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.LastAccessTime].Width;
			mw.Columns[AppDefs.ColumnIdnLastModificationTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.LastModificationTime].Width;
			mw.Columns[AppDefs.ColumnIdnExpiryTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.ExpiryTime].Width;
			mw.Columns[AppDefs.ColumnIdnUuid].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Uuid].Width;
			mw.Columns[AppDefs.ColumnIdnAttachment].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnID.Attachment].Width;

			Debug.Assert(m_bSimpleTanView == m_menuViewTanSimpleList.Checked);
			mw.TANView.UseSimpleView = m_bSimpleTanView;
			Debug.Assert(m_bShowTanIndices == m_menuViewTanIndices.Checked);
			mw.TANView.ShowIndices = m_bShowTanIndices;

			mw.Columns[PwDefs.TitleField].HideWithAsterisks = m_menuViewHideTitles.Checked;
			mw.Columns[PwDefs.UserNameField].HideWithAsterisks = m_menuViewHideUserNames.Checked;
			mw.Columns[PwDefs.PasswordField].HideWithAsterisks = m_menuViewHidePasswords.Checked;
			mw.Columns[PwDefs.UrlField].HideWithAsterisks = m_menuViewHideURLs.Checked;
			mw.Columns[PwDefs.NotesField].HideWithAsterisks = m_menuViewHideNotes.Checked;

			SaveDisplayIndex(mw, PwDefs.TitleField, AppDefs.ColumnID.Title);
			SaveDisplayIndex(mw, PwDefs.UserNameField, AppDefs.ColumnID.UserName);
			SaveDisplayIndex(mw, PwDefs.PasswordField, AppDefs.ColumnID.Password);
			SaveDisplayIndex(mw, PwDefs.UrlField, AppDefs.ColumnID.Url);
			SaveDisplayIndex(mw, PwDefs.NotesField, AppDefs.ColumnID.Notes);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnAttachment, AppDefs.ColumnID.Attachment);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnCreationTime, AppDefs.ColumnID.CreationTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnExpiryTime, AppDefs.ColumnID.ExpiryTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnLastAccessTime, AppDefs.ColumnID.LastAccessTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnLastModificationTime, AppDefs.ColumnID.LastModificationTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnUuid, AppDefs.ColumnID.Uuid);

			Program.Config.MainWindow.ListSorting = m_pListSorter;

			Program.Config.Application.MostRecentlyUsed.MaxItemCount = m_mruList.MaxItemCount;
			Program.Config.Application.MostRecentlyUsed.Items.Clear();
			for(uint uMru = 0; uMru < m_mruList.ItemCount; ++uMru)
			{
				KeyValuePair<string, object> kvpMru = m_mruList.GetItem(uMru);
				IOConnectionInfo ioMru = kvpMru.Value as IOConnectionInfo;
				if(ioMru != null)
					Program.Config.Application.MostRecentlyUsed.Items.Add(ioMru);
				else { Debug.Assert(false); }
			}

			mw.ShowGridLines = m_lvEntries.GridLines;

			AppConfigSerializer.Save(Program.Config);
		}

		private void SaveDisplayIndex(AceMainWindow mw, string strColID,
			AppDefs.ColumnID colID)
		{
			mw.Columns[strColID].DisplayIndex =
				m_lvEntries.Columns[(int)colID].DisplayIndex;
		}

		private void RestoreDisplayIndex(AceMainWindow mw, string strColID,
			AppDefs.ColumnID colID)
		{
			try
			{
				int nIndex = mw.Columns[strColID].DisplayIndex;

				if((nIndex >= 0) && (nIndex < (int)AppDefs.ColumnID.Count))
					m_lvEntries.Columns[(int)colID].DisplayIndex = nIndex;
			}
			catch(Exception) { Debug.Assert(false); }
		}

		private void SaveWindowPositionAndSize()
		{
			FormWindowState ws = this.WindowState;

			if(ws == FormWindowState.Normal)
			{
				Program.Config.MainWindow.X = this.Location.X;
				Program.Config.MainWindow.Y = this.Location.Y;
				Program.Config.MainWindow.Width = this.Size.Width;
				Program.Config.MainWindow.Height = this.Size.Height;
			}

			if((ws == FormWindowState.Normal) || (ws == FormWindowState.Maximized))
			{
				Program.Config.MainWindow.SplitterHorizontalPosition =
					m_splitHorizontal.SplitterDistance;
				Program.Config.MainWindow.SplitterVerticalPosition =
					m_splitVertical.SplitterDistance;
			}

			Program.Config.MainWindow.Maximized = (ws == FormWindowState.Maximized);
		}

		/// <summary>
		/// Update the UI state, i.e. enable/disable menu items depending on the state
		/// of the database (open, closed, locked, modified) and the selected items in
		/// the groups and entries list. You must call this function after all
		/// state-changing operations. For example, if you add a new entry the state
		/// needs to be updated (as the database has been modified) and you must call
		/// this function.
		/// </summary>
		/// <param name="bSetModified">If this parameter is <c>true</c>, the currently
		/// opened database is marked as modified.</param>
		private void UpdateUIState(bool bSetModified)
		{
			NotifyUserActivity();

			bool bDatabaseOpened = m_docMgr.ActiveDatabase.IsOpen;

			if(bDatabaseOpened && bSetModified)
				m_docMgr.ActiveDatabase.Modified = true;

			bool bGroupsEnabled = m_tvGroups.Enabled;
			if(bGroupsEnabled && (!bDatabaseOpened))
			{
				m_tvGroups.BackColor = AppDefs.ColorControlDisabled;
				m_tvGroups.Enabled = false;
			}
			else if((!bGroupsEnabled) && bDatabaseOpened)
			{
				m_tvGroups.Enabled = true;
				m_tvGroups.BackColor = AppDefs.ColorControlNormal;
			}

			m_lvEntries.Enabled = bDatabaseOpened;

			int nEntriesCount = m_lvEntries.Items.Count;
			int nEntriesSelected = m_lvEntries.SelectedIndices.Count;

			m_statusPartSelected.Text = nEntriesSelected.ToString() +
				" " + KPRes.OfLower + " " + nEntriesCount.ToString() +
				" " + KPRes.SelectedLower;
			m_statusPartInfo.Text = KPRes.Ready;

			string strWindowText = string.Empty;
			string strNtfText = string.Empty;

			if(IsFileLocked(null))
			{
				IOConnectionInfo iocLck = m_docMgr.ActiveDocument.LockedIoc;

				strWindowText = iocLck.Path + " [" + KPRes.Locked +
					"] - " + PwDefs.ProductName;

				string strNtfPre = PwDefs.ShortProductName + " - " +
					KPRes.WorkspaceLocked + MessageService.NewLine;

				strNtfText = strNtfPre + WinUtil.CompactPath(iocLck.Path, 63 -
					strNtfPre.Length);

				m_ntfTray.Icon = Properties.Resources.QuadLocked;
			}
			else if(bDatabaseOpened == false)
			{
				strWindowText = PwDefs.ProductName;
				strNtfText = strWindowText;

				m_ntfTray.Icon = Properties.Resources.QuadNormal;
			}
			else // Database open and not locked
			{
				string strFileDesc;
				if(Program.Config.MainWindow.ShowFullPathInTitle)
					strFileDesc = m_docMgr.ActiveDatabase.IOConnectionInfo.GetDisplayName();
				else
					strFileDesc = UrlUtil.GetFileName(
						m_docMgr.ActiveDatabase.IOConnectionInfo.Path);

				strFileDesc = WinUtil.CompactPath(strFileDesc, 63 -
					PwDefs.ProductName.Length - 4);

				if(m_docMgr.ActiveDatabase.Modified)
					strWindowText = strFileDesc + "* - " + PwDefs.ProductName;
				else
					strWindowText = strFileDesc + " - " + PwDefs.ProductName;

				string strNtfPre = PwDefs.ProductName + MessageService.NewLine;
				strNtfText = strNtfPre + WinUtil.CompactPath(
					m_docMgr.ActiveDatabase.IOConnectionInfo.Path, 63 - strNtfPre.Length);

				m_ntfTray.Icon = Properties.Resources.QuadNormal;
			}

			// Clip the strings again (it could be that a translator used
			// a string in KPRes that is too long to be displayed)
			this.Text = StrUtil.CompactString3Dots(strWindowText, 63);
			m_ntfTray.Text = StrUtil.CompactString3Dots(strNtfText, 63);

			// Main menu
			m_menuFileClose.Enabled = m_menuFileSave.Enabled = m_menuFileSaveAs.Enabled =
				m_menuFileSaveAsLocal.Enabled = m_menuFileSaveAsUrl.Enabled =
				m_menuFileDbSettings.Enabled = m_menuFileChangeMasterKey.Enabled =
				m_menuFilePrint.Enabled = bDatabaseOpened;

			m_menuFileLock.Enabled = m_tbLockWorkspace.Enabled =
				(bDatabaseOpened || IsFileLocked(null));

			m_menuEditFind.Enabled = m_menuToolsGeneratePwList.Enabled =
				m_menuToolsTanWizard.Enabled =
				m_menuEditShowAllEntries.Enabled = m_menuEditShowExpired.Enabled =
				m_menuToolsDbMaintenance.Enabled = bDatabaseOpened;

			m_menuFileImport.Enabled = bDatabaseOpened;

			m_menuFileExport.Enabled = m_menuFileExportXML.Enabled =
				m_menuFileExportHtml.Enabled = m_menuFileExportKdb3.Enabled =
				m_menuFileExportUseXsl.Enabled = bDatabaseOpened;

			m_menuFileSynchronize.Enabled = bDatabaseOpened &&
				m_docMgr.ActiveDatabase.IOConnectionInfo.IsLocalFile() &&
				(m_docMgr.ActiveDatabase.IOConnectionInfo.Path.Length > 0);

			m_ctxGroupAdd.Enabled = m_ctxGroupEdit.Enabled =
				m_ctxGroupDelete.Enabled = m_ctxGroupFind.Enabled =
				m_ctxGroupPrint.Enabled = bDatabaseOpened;

			m_ctxGroupMoveToTop.Enabled = m_ctxGroupMoveOneUp.Enabled =
				m_ctxGroupMoveOneDown.Enabled = m_ctxGroupMoveToBottom.Enabled =
				bDatabaseOpened;

			m_tbSaveDatabase.Enabled = m_tbFind.Enabled = m_tbViewsShowAll.Enabled =
				m_tbViewsShowExpired.Enabled = bDatabaseOpened;

			m_tbQuickFind.Enabled = bDatabaseOpened;

			m_ctxEntryAdd.Enabled = m_tbAddEntry.Enabled = bDatabaseOpened;
			m_ctxEntryEdit.Enabled = (nEntriesSelected == 1);
			m_ctxEntryDelete.Enabled = (nEntriesSelected > 0);

			m_ctxEntryCopyUserName.Enabled = m_ctxEntryCopyPassword.Enabled =
				m_tbCopyUserName.Enabled = m_tbCopyPassword.Enabled =
				m_ctxEntryCopyUrl.Enabled = m_ctxEntryUrlOpenInInternal.Enabled =
				(nEntriesSelected == 1);

			m_ctxEntryOpenUrl.Enabled = m_ctxEntryDuplicate.Enabled =
				m_ctxEntryMassSetIcon.Enabled = m_ctxEntrySelectedPrint.Enabled =
				(nEntriesSelected > 0);

			m_ctxEntryMoveToTop.Enabled = m_ctxEntryMoveToBottom.Enabled =
				((m_pListSorter.Column < 0) && (nEntriesSelected > 0));
			m_ctxEntryMoveOneDown.Enabled = m_ctxEntryMoveOneUp.Enabled =
				((m_pListSorter.Column < 0) && (nEntriesSelected == 1));

			m_ctxEntrySelectAll.Enabled = (bDatabaseOpened && (nEntriesCount > 0));

			m_tbSaveDatabase.Image = m_docMgr.ActiveDatabase.Modified ? m_imgFileSaveEnabled :
				m_imgFileSaveDisabled;

			m_ctxEntryClipCopy.Enabled = (nEntriesSelected > 0);
			m_ctxEntryClipPaste.Enabled = Clipboard.ContainsData(EntryUtil.ClipFormatEntries);

			m_ctxEntryColorStandard.Enabled = m_ctxEntryColorLightRed.Enabled =
				m_ctxEntryColorLightGreen.Enabled = m_ctxEntryColorLightBlue.Enabled =
				m_ctxEntryColorLightYellow.Enabled = m_ctxEntryColorCustom.Enabled =
				(nEntriesSelected > 0);

			PwEntry pe = GetSelectedEntry(true);
			ShowEntryDetails(pe);

			if(pe != null)
			{
				m_ctxEntryPerformAutoType.Enabled = (pe.AutoType.Enabled &&
					(nEntriesSelected == 1));
				m_ctxEntrySaveAttachedFiles.Enabled = ((pe.Binaries.UCount > 0) ||
					(nEntriesSelected >= 2));
			}
			else // pe == null
			{
				m_ctxEntryPerformAutoType.Enabled = false;
				m_ctxEntrySaveAttachedFiles.Enabled = false;
			}

			bool bIsOneTan = (nEntriesSelected == 1);
			if(pe != null) bIsOneTan &= PwDefs.IsTanEntry(pe);
			else bIsOneTan = false;

			m_ctxEntryCopyUserName.Visible = !bIsOneTan;
			m_ctxEntryUrl.Visible = !bIsOneTan;
			m_ctxEntrySaveAttachedFiles.Visible = !bIsOneTan;
			m_ctxEntryCopyPassword.Text = (bIsOneTan ? KPRes.CopyTanMenu :
				KPRes.CopyPasswordMenu);

			string strLockUnlock = IsFileLocked(null) ? KPRes.LockMenuUnlock :
				KPRes.LockMenuLock;
			m_menuFileLock.Text = strLockUnlock;
			m_tbLockWorkspace.Text = strLockUnlock.Replace(@"&", string.Empty);

			m_tabMain.Visible = m_tbSaveAll.Visible = m_tbCloseTab.Visible =
				(m_docMgr.DocumentCount > 1);
			m_tbCloseTab.Enabled = bDatabaseOpened;

			bool bAtLeastOneModified = false;
			foreach(TabPage tabPage in m_tabMain.TabPages)
			{
				DocumentStateEx dsPage = (DocumentStateEx)tabPage.Tag;
				if(dsPage.Database.Modified) bAtLeastOneModified = true;

				string strTabText = tabPage.Text;
				if(dsPage.Database.Modified && !strTabText.EndsWith("*"))
					tabPage.Text += "*";
				else if(!dsPage.Database.Modified && strTabText.EndsWith("*"))
					tabPage.Text = strTabText.Substring(0, strTabText.Length - 1);
			}

			m_tbSaveAll.Image = (bAtLeastOneModified ? m_imgFileSaveAllEnabled :
				m_imgFileSaveAllDisabled);

			UpdateUITabs();
			UpdateLinkedMenuItems();
		}

		/// <summary>
		/// Set the main status bar text.
		/// </summary>
		/// <param name="strStatusText">New status bar text.</param>
		public void SetStatusEx(string strStatusText)
		{
			if(strStatusText == null) m_statusPartInfo.Text = KPRes.Ready;
			else m_statusPartInfo.Text = strStatusText;
		}

		private void UpdateClipboardStatus()
		{
			if(m_nClipClearCur > 0)
				m_statusClipboard.Value = (m_nClipClearCur * 100) / m_nClipClearMax;
			else if(m_nClipClearCur == 0)
				m_statusClipboard.Visible = false;
		}

		/// <summary>
		/// Start the clipboard countdown (set the current tick count to the
		/// maximum value and decrease it each second -- at 0 the clipboard
		/// is cleared automatically). This function is asynchronous.
		/// </summary>
		public void StartClipboardCountdown()
		{
			if(m_nClipClearMax >= 0)
			{
				m_nClipClearCur = m_nClipClearMax;

				m_statusClipboard.Visible = true;
				UpdateClipboardStatus();

				string strText = KPRes.ClipboardDataCopied + " " +
					KPRes.ClipboardClearInSeconds + ".";
				strText = strText.Replace(@"[PARAM]", m_nClipClearMax.ToString());

				SetStatusEx(strText);

				// if(m_ntfTray.Visible)
				//	m_ntfTray.ShowBalloonTip(0, KPRes.ClipboardAutoClear,
				//		strText, ToolTipIcon.Info);
			}
		}

		/// <summary>
		/// Gets the focused or first selected entry.
		/// </summary>
		/// <returns></returns>
		public PwEntry GetSelectedEntry(bool bRequireSelected)
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return null;

			if(!bRequireSelected)
			{
				ListViewItem lviFocused = m_lvEntries.FocusedItem;
				if(lviFocused != null) return (PwEntry)lviFocused.Tag;
			}

			ListView.SelectedListViewItemCollection coll = m_lvEntries.SelectedItems;
			if(coll.Count > 0)
			{
				ListViewItem lvi = coll[0];
				if(lvi != null) return (PwEntry)lvi.Tag;
			}

			return null;
		}

		/// <summary>
		/// Get all selected entries.
		/// </summary>
		/// <returns>A list of all selected entries.</returns>
		public PwEntry[] GetSelectedEntries()
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return null;

			ListView.SelectedListViewItemCollection coll = m_lvEntries.SelectedItems;

			if((coll == null) || (coll.Count == 0)) return null;

			PwEntry[] vSelected = new PwEntry[coll.Count];
			for(int i = 0; i < coll.Count; i++)
				vSelected[i] = (PwEntry)coll[i].Tag;

			return vSelected;
		}

		/// <summary>
		/// Get the currently selected group. The selected <c>TreeNode</c> is
		/// automatically translated to a <c>PwGroup</c>.
		/// </summary>
		/// <returns>Selected <c>PwGroup</c>.</returns>
		public PwGroup GetSelectedGroup()
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return null;

			TreeNode tn = m_tvGroups.SelectedNode;
			if(tn == null) return null;
			return (PwGroup)tn.Tag;
		}

		private ListViewItem AddEntryToList(PwEntry pe)
		{
			if(pe == null) return null;

			ListViewItem lvi = new ListViewItem();
			lvi.Tag = pe;

			if(pe.Expires && (pe.ExpiryTime <= m_dtCachedNow))
			{
				lvi.ImageIndex = (int)PwIcon.Expired;
				lvi.Font = m_fontExpired;
			}
			else if(pe.CustomIconUuid == PwUuid.Zero)
				lvi.ImageIndex = (int)pe.IconID;
			else
				lvi.ImageIndex = (int)PwIcon.Count +
					m_docMgr.ActiveDatabase.GetCustomIconIndex(pe.CustomIconUuid);

			if(m_bEntryGrouping)
			{
				PwGroup pgContainer = pe.ParentGroup;
				PwGroup pgLast = (m_lvgLastEntryGroup != null) ? (PwGroup)m_lvgLastEntryGroup.Tag : null;

				Debug.Assert(pgContainer != null);
				if(pgContainer != null)
				{
					if(pgContainer != pgLast)
					{
						m_lvgLastEntryGroup = new ListViewGroup(
							pgContainer.GetFullPath());
						m_lvgLastEntryGroup.Tag = pgContainer;

						m_lvEntries.Groups.Add(m_lvgLastEntryGroup);
					}

					lvi.Group = m_lvgLastEntryGroup;
				}
			}

			if(!pe.ForegroundColor.IsEmpty)
				lvi.ForeColor = pe.ForegroundColor;
			if(!pe.BackgroundColor.IsEmpty)
				lvi.BackColor = pe.BackgroundColor;

			m_bOnlyTans &= PwDefs.IsTanEntry(pe);
			if(m_bShowTanIndices && m_bOnlyTans)
			{
				string strIndex = pe.Strings.ReadSafe(PwDefs.TanIndexField);

				if(strIndex.Length > 0) lvi.Text = strIndex;
				else lvi.Text = PwDefs.TanTitle;

				m_lvEntries.Items.Add(lvi);
			}
			else
			{
				if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Title].Width > 0)
				{
					if(m_viewHideFields.ProtectTitle) lvi.Text = PwDefs.HiddenPassword;
					else lvi.Text = pe.Strings.ReadSafe(PwDefs.TitleField);
				}
				m_lvEntries.Items.Add(lvi);
			}

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.UserName].Width > 0)
			{
				if(m_viewHideFields.ProtectUserName) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.UserNameField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Password].Width > 0)
			{
				if(m_viewHideFields.ProtectPassword) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.PasswordField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Url].Width > 0)
			{
				if(m_viewHideFields.ProtectUrl) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.UrlField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Notes].Width > 0)
			{
				if(m_viewHideFields.ProtectNotes) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else
				{
					string strNotesData = pe.Strings.ReadSafe(PwDefs.NotesField);
					string strNotesNoR = strNotesData.Replace("\r", string.Empty);
					lvi.SubItems.Add(strNotesNoR.Replace("\n", " "));
				}
			}
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.CreationTime].Width > 0)
				lvi.SubItems.Add(pe.CreationTime.ToString());
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.LastAccessTime].Width > 0)
				lvi.SubItems.Add(pe.LastAccessTime.ToString());
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.LastModificationTime].Width > 0)
				lvi.SubItems.Add(pe.LastModificationTime.ToString());
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.ExpiryTime].Width > 0)
			{
				if(pe.Expires) lvi.SubItems.Add(pe.ExpiryTime.ToString());
				else lvi.SubItems.Add(m_strNeverExpiresText);
			}
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Uuid].Width > 0)
				lvi.SubItems.Add(pe.Uuid.ToHexString());
			else lvi.SubItems.Add(string.Empty);

			if(m_lvEntries.Columns[(int)AppDefs.ColumnID.Attachment].Width > 0)
				lvi.SubItems.Add(pe.Binaries.UCount.ToString());
			else lvi.SubItems.Add(string.Empty);

			return lvi;
		}

		/// <summary>
		/// Update the group list. This function completely rebuilds the groups
		/// view. You must call this function after you made any changes to the
		/// groups structure of the currently opened database.
		/// </summary>
		/// <param name="pgNewSelected">If this parameter is <c>null</c>, the
		/// previously selected group is selected again (after the list was
		/// rebuilt). If this parameter is non-<c>null</c>, the specified
		/// <c>PwGroup</c> is selected after the function returns.</param>
		private void UpdateGroupList(PwGroup pgNewSelected)
		{
			NotifyUserActivity();

			PwDatabase pwDb = m_docMgr.ActiveDatabase;

			PwGroup pg = (pgNewSelected == null) ? GetSelectedGroup() : pgNewSelected;

			UpdateImageLists();

			m_tvGroups.BeginUpdate();
			m_tvGroups.Nodes.Clear();

			TreeNode tnRoot = null;
			if(pwDb.RootGroup != null)
			{
				tnRoot = new TreeNode(pwDb.RootGroup.Name,
					(int)pwDb.RootGroup.IconID, (int)pwDb.RootGroup.IconID);
				tnRoot.Tag = pwDb.RootGroup;
				tnRoot.NodeFont = new Font(m_tvGroups.Font, FontStyle.Bold);
				m_tvGroups.Nodes.Add(tnRoot);
			}

			m_dtCachedNow = DateTime.Now;

			TreeNode tnSelected = null;
			RecursiveAddGroup(tnRoot, pwDb.RootGroup, pg, ref tnSelected);

			if(tnRoot != null) tnRoot.Expand();

			m_tvGroups.EndUpdate();

			if(tnSelected != null) m_tvGroups.SelectedNode = tnSelected;
			else if(m_tvGroups.Nodes.Count > 0)
				m_tvGroups.SelectedNode = m_tvGroups.Nodes[0];
		}

		/// <summary>
		/// Update the entries list. This function completely rebuilds the entries
		/// list. You must call this function after you've made any changes to
		/// the entries of the currently selected group. Note that if you only
		/// made small changes (like editing an existing entry), the
		/// <c>RefreshEntriesList</c> function could be a better choice, as it only
		/// updates currently listed items and doesn't rebuild the whole list as
		/// <c>UpdateEntriesList</c>.
		/// </summary>
		/// <param name="pgSelected">Group whose entries should be shown. If this
		/// parameter is <c>null</c>, the entries of the currently selected group
		/// (groups view) are displayed, otherwise the entries of the <c>pgSelected</c>
		/// group are displayed.</param>
		private void UpdateEntryList(PwGroup pgSelected, bool bOnlyUpdateCurrentlyShown)
		{
			NotifyUserActivity();

			UpdateImageLists();

			PwEntry peTop = GetTopEntry(), peFocused = GetSelectedEntry(false);
			PwEntry[] vSelected = GetSelectedEntries();

			bool bSubEntries = Program.Config.MainWindow.ShowEntriesOfSubGroups;

			PwGroup pg = (pgSelected != null) ? pgSelected : GetSelectedGroup();
			
			// Disabled for now -- requires the group returned by
			// GetCurrentEntries to be sorted by list view groups
			// if(bOnlyUpdateCurrentlyShown)
			// {
			//	Debug.Assert(pgSelected == null);
			//	pg = GetCurrentEntries();
			// }

			PwObjectList<PwEntry> pwlSource = ((pg != null) ?
				pg.GetEntries(bSubEntries) : new PwObjectList<PwEntry>());

			m_lvEntries.BeginUpdate();
			m_lvEntries.Items.Clear();
			m_bOnlyTans = true;

			m_bEntryGrouping = (((pg != null) ? pg.IsVirtual : false) ||
				bSubEntries);
			m_lvgLastEntryGroup = null;
			m_lvEntries.ShowGroups = m_bEntryGrouping;

			int nTopIndex = -1;
			ListViewItem lviFocused = null;

			m_dtCachedNow = DateTime.Now;
			if(pg != null)
			{
				foreach(PwEntry pe in pwlSource)
				{
					ListViewItem lvi = AddEntryToList(pe);

					if(vSelected != null)
					{
						if(Array.IndexOf(vSelected, pe) >= 0)
							lvi.Selected = true;
					}

					if(pe == peTop) nTopIndex = m_lvEntries.Items.Count - 1;
					if(pe == peFocused) lviFocused = lvi;
				}
			}

			if(nTopIndex >= 0)
			{
				m_lvEntries.EnsureVisible(m_lvEntries.Items.Count - 1);
				m_lvEntries.EnsureVisible(nTopIndex);
			}

			if(lviFocused != null) m_lvEntries.FocusedItem = lviFocused;

			View view = m_lvEntries.View;
			if(m_bSimpleTanView)
			{
				if(m_lvEntries.Items.Count == 0)
					m_lvEntries.View = View.Details;
				else if(m_bOnlyTans && (view != View.List))
				{
					// SortPasswordList(false, 0, false);
					m_lvEntries.View = View.List;
				}
				else if(!m_bOnlyTans && (view != View.Details))
					m_lvEntries.View = View.Details;
			}
			else // m_bSimpleTANView == false
			{
				if(view != View.Details)
					m_lvEntries.View = View.Details;
			}

			m_lvEntries.EndUpdate();
		}

		/// <summary>
		/// Refresh the entries list. All currently displayed entries are updated.
		/// If you made changes to the list that change the number of visible entries
		/// (like adding or removing an entry), you must use the <c>UpdatePasswordList</c>
		/// function instead.
		/// </summary>
		public void RefreshEntriesList()
		{
			int nItemCount = m_lvEntries.Items.Count;
			if(nItemCount <= 0) return;

			PwEntry peTop = GetTopEntry();
			PwEntry peFocused = GetSelectedEntry(false);

			UpdateImageLists();

			PwEntry[] vSelected = GetSelectedEntries();
			if(vSelected == null)
				vSelected = new PwEntry[1]{ new PwEntry(null, false, false) };

			PwEntry[] vList = new PwEntry[nItemCount];
			for(int iEnum = 0; iEnum < nItemCount; iEnum++)
				vList[iEnum] = (PwEntry)m_lvEntries.Items[iEnum].Tag;

			m_lvEntries.BeginUpdate();
			m_lvEntries.Items.Clear();

			int nTopIndex = -1;
			ListViewItem lviFocused = null;

			m_dtCachedNow = DateTime.Now;
			for(int iAdd = 0; iAdd < nItemCount; iAdd++)
			{
				PwEntry pe = vList[iAdd];

				ListViewItem lvi = AddEntryToList(pe);

				if(pe == peTop) nTopIndex = iAdd;
				if(pe == peFocused) lviFocused = lvi;

				if(Array.IndexOf(vSelected, pe) >= 0)
					lvi.Selected = true;
			}

			if(nTopIndex >= 0)
			{
				m_lvEntries.EnsureVisible(m_lvEntries.Items.Count - 1);
				m_lvEntries.EnsureVisible(nTopIndex);
			}

			if(lviFocused != null) m_lvEntries.FocusedItem = lviFocused;

			m_lvEntries.EndUpdate();
		}

		private PwEntry GetTopEntry()
		{
			PwEntry peTop = null;

			if(m_lvEntries.Items.Count == 0) return null;

			try
			{
				ListViewItem lviTop = m_lvEntries.TopItem;
				if(lviTop != null) peTop = (PwEntry)lviTop.Tag;
			}
			catch(Exception) { peTop = null; }

			return peTop;
		}

		private void RecursiveAddGroup(TreeNode tnParent, PwGroup pgContainer, PwGroup pgFind, ref TreeNode tnFound)
		{
			if(pgContainer == null) return;

			TreeNodeCollection tnc;
			if(tnParent == null) tnc = m_tvGroups.Nodes;
			else tnc = tnParent.Nodes;

			foreach(PwGroup pg in pgContainer.Groups)
			{
				if(pg.Expires && (pg.ExpiryTime <= m_dtCachedNow))
					pg.IconID = PwIcon.Expired;

				string strName = pg.Name;

				int nIconID = (pg.CustomIconUuid != PwUuid.Zero) ? ((int)PwIcon.Count +
					m_docMgr.ActiveDatabase.GetCustomIconIndex(pg.CustomIconUuid)) :
					(int)pg.IconID;

				TreeNode tn = new TreeNode(strName, nIconID, nIconID);
				tn.Tag = pg;
				tnc.Add(tn);

				RecursiveAddGroup(tn, pg, pgFind, ref tnFound);

				if(tn.Nodes.Count > 0)
				{
					if((tn.IsExpanded) && (!pg.IsExpanded)) tn.Collapse();
					else if((!tn.IsExpanded) && (pg.IsExpanded)) tn.Expand();
				}

				if(pg == pgFind) tnFound = tn;
			}
		}

		private void SortPasswordList(bool bEnableSorting, int nColumn, bool bUpdateEntryList)
		{
			if(bEnableSorting)
			{
				int nOldColumn = m_pListSorter.Column;
				SortOrder sortOrder = m_pListSorter.Order;

				if(nColumn == nOldColumn)
				{
					if(sortOrder == SortOrder.None)
						sortOrder = SortOrder.Ascending;
					else if(sortOrder == SortOrder.Ascending)
						sortOrder = SortOrder.Descending;
					else if(sortOrder == SortOrder.Descending)
						sortOrder = SortOrder.None;
					else { Debug.Assert(false); }
				}
				else sortOrder = SortOrder.Ascending;

				if(sortOrder != SortOrder.None)
				{
					m_pListSorter = new ListSorter(nColumn, sortOrder);
					m_lvEntries.ListViewItemSorter = m_pListSorter;
				}
				else
				{
					m_pListSorter = new ListSorter();
					m_lvEntries.ListViewItemSorter = null;

					if(bUpdateEntryList) UpdateEntryList(null, true);
				}
			}
			else // Disable sorting
			{
				m_pListSorter = new ListSorter();
				m_lvEntries.ListViewItemSorter = null;

				if(bUpdateEntryList) UpdateEntryList(null, true);
			}

			UpdateColumnSortingIcons();
		}

		private void UpdateColumnSortingIcons()
		{
			if(m_lvEntries.SmallImageList == null) return;

			if(m_pListSorter.Column < 0) { Debug.Assert(m_lvEntries.ListViewItemSorter == null); }

			foreach(ColumnHeader ch in m_lvEntries.Columns)
			{
				if(ch.Index == m_pListSorter.Column)
				{
					if(m_pListSorter.Order == SortOrder.None)
						ch.ImageIndex = -1;
					else if(m_pListSorter.Order == SortOrder.Ascending)
						ch.ImageIndex = (int)PwIcon.SortUpArrow;
					else if(m_pListSorter.Order == SortOrder.Descending)
						ch.ImageIndex = (int)PwIcon.SortDownArrow;
				}
				else ch.ImageIndex = -1;
			}
		}

		private void ShowEntryView(bool bShow)
		{
			m_menuViewShowEntryView.Checked = bShow;

			Program.Config.MainWindow.EntryView.Show = bShow;

			m_richEntryView.Visible = bShow;
			m_splitHorizontal.Panel2Collapsed = !bShow;
		}

		private void ShowEntryDetails(PwEntry pe)
		{
			if(pe == null)
			{
				m_richEntryView.Text = string.Empty;
				return;
			}

			AceFont af = Program.Config.UI.StandardFont;
			string strFontFace = (af.OverrideUIDefault ? af.Family : "Microsoft Sans Serif");
			float fFontSize = (af.OverrideUIDefault ? af.ToFont().SizeInPoints : 8);

			string strItemSeparator = (m_splitHorizontal.Orientation == Orientation.Horizontal) ?
				", " : "\\par ";

			StringBuilder sb = new StringBuilder();
			StrUtil.InitRtf(sb, strFontFace, fFontSize);

			sb.Append("\\b ");
			sb.Append(KPRes.Group);
			sb.Append(":\\b0  ");

			PwGroup pg = pe.ParentGroup;
			if(pg != null) sb.Append(StrUtil.MakeRtfString(pg.Name));

			EvAppendEntryField(sb, strItemSeparator, KPRes.Title,
				m_viewHideFields.ProtectTitle ? PwDefs.HiddenPassword :
				StrUtil.MakeRtfString(pe.Strings.ReadSafe(PwDefs.TitleField)));
			EvAppendEntryField(sb, strItemSeparator, KPRes.UserName,
				m_viewHideFields.ProtectUserName ? PwDefs.HiddenPassword :
				StrUtil.MakeRtfString(pe.Strings.ReadSafe(PwDefs.UserNameField)));
			EvAppendEntryField(sb, strItemSeparator, KPRes.Password,
				m_viewHideFields.ProtectPassword ? PwDefs.HiddenPassword :
				StrUtil.MakeRtfString(pe.Strings.ReadSafe(PwDefs.PasswordField)));
			EvAppendEntryField(sb, strItemSeparator, KPRes.URL,
				m_viewHideFields.ProtectUrl ? PwDefs.HiddenPassword :
				StrUtil.MakeRtfString(pe.Strings.ReadSafe(PwDefs.UrlField)));

			foreach(KeyValuePair<string, ProtectedString> kvp in pe.Strings)
			{
				if(PwDefs.IsStandardField(kvp.Key)) continue;

				EvAppendEntryField(sb, strItemSeparator, kvp.Key,
					StrUtil.MakeRtfString(pe.Strings.ReadSafeEx(kvp.Key)));
			}

			EvAppendEntryField(sb, strItemSeparator, KPRes.CreationTime,
				StrUtil.MakeRtfString(TimeUtil.ToDisplayString(pe.CreationTime)));
			EvAppendEntryField(sb, strItemSeparator, KPRes.LastAccessTime,
				StrUtil.MakeRtfString(TimeUtil.ToDisplayString(pe.LastAccessTime)));
			EvAppendEntryField(sb, strItemSeparator, KPRes.LastModificationTime,
				StrUtil.MakeRtfString(TimeUtil.ToDisplayString(pe.LastModificationTime)));

			if(pe.Expires)
				EvAppendEntryField(sb, strItemSeparator, KPRes.ExpiryTime,
					StrUtil.MakeRtfString(TimeUtil.ToDisplayString(pe.ExpiryTime)));

			if(pe.Binaries.UCount > 0)
			{
				StringBuilder sbBinaries = new StringBuilder();

				foreach(KeyValuePair<string, ProtectedBinary> kvpBin in pe.Binaries)
				{
					if(sbBinaries.Length > 0) sbBinaries.Append(", ");
					sbBinaries.Append(kvpBin.Key);
				}

				EvAppendEntryField(sb, strItemSeparator, KPRes.Attachments,
					StrUtil.MakeRtfString(sbBinaries.ToString()));
			}

			EvAppendEntryField(sb, strItemSeparator, KPRes.UrlOverride,
				StrUtil.MakeRtfString(pe.OverrideUrl));

			string strNotes = (m_viewHideFields.ProtectNotes ?
				PwDefs.HiddenPassword : pe.Strings.ReadSafe(PwDefs.NotesField));
			if(strNotes.Length != 0)
			{
				sb.Append("\\par \\par ");

				strNotes = StrUtil.MakeRtfString(strNotes);

				strNotes = strNotes.Replace("<b>", "\\b ");
				strNotes = strNotes.Replace("</b>", "\\b0 ");
				strNotes = strNotes.Replace("<i>", "\\i ");
				strNotes = strNotes.Replace("</i>", "\\i0 ");
				strNotes = strNotes.Replace("<u>", "\\ul ");
				strNotes = strNotes.Replace("</u>", "\\ul0 ");

				sb.Append(strNotes);
			}

			sb.Append("\\pard }");
			m_richEntryView.Rtf = sb.ToString();
		}

		private static void EvAppendEntryField(StringBuilder sb,
			string strItemSeparator, string strName, string strValue)
		{
			if(strValue.Length == 0) return;

			sb.Append(strItemSeparator);
			sb.Append("\\b ");
			sb.Append(strName);
			sb.Append(":\\b0  ");
			sb.Append(strValue);
		}

		private void PerformDefaultAction(object sender, EventArgs e, PwEntry pe, AppDefs.ColumnID colID)
		{
			Debug.Assert(pe != null); if(pe == null) return;

			if(this.DefaultEntryAction != null)
			{
				CancelEntryEventArgs args = new CancelEntryEventArgs(pe, colID);
				this.DefaultEntryAction(sender, args);
				if(args.Cancel) return;
			}

			bool bMinimize = Program.Config.MainWindow.MinimizeAfterClipboardCopy;
			Form frmMin = (bMinimize ? this : null);

			switch(colID)
			{
				case AppDefs.ColumnID.Title:
					if(PwDefs.IsTanEntry(pe))
						OnEntryCopyPassword(sender, e);
					else
						OnEntryEdit(sender, e);
					break;
				case AppDefs.ColumnID.UserName:
					OnEntryCopyUserName(sender, e);
					break;
				case AppDefs.ColumnID.Password:
					OnEntryCopyPassword(sender, e);
					break;
				case AppDefs.ColumnID.Url:
					OnEntryOpenUrl(sender, e);
					break;
				case AppDefs.ColumnID.Notes:
					ClipboardUtil.CopyAndMinimize(pe.Strings.ReadSafe(PwDefs.NotesField),
						true, frmMin);
					StartClipboardCountdown();
					break;
				case AppDefs.ColumnID.CreationTime:
					ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(pe.CreationTime),
						true, frmMin);
					StartClipboardCountdown();
					break;
				case AppDefs.ColumnID.LastAccessTime:
					ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(pe.LastAccessTime),
						true, frmMin);
					StartClipboardCountdown();
					break;
				case AppDefs.ColumnID.LastModificationTime:
					ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(pe.LastModificationTime),
						true, frmMin);
					StartClipboardCountdown();
					break;
				case AppDefs.ColumnID.ExpiryTime:
					if(pe.Expires)
						ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(pe.ExpiryTime),
							true, frmMin);
					else
						ClipboardUtil.CopyAndMinimize(KPRes.NeverExpires,
							true, frmMin);
					StartClipboardCountdown();
					break;
				case AppDefs.ColumnID.Attachment:
					break;
				case AppDefs.ColumnID.Uuid:
					ClipboardUtil.CopyAndMinimize(pe.Uuid.ToHexString(),
						true, frmMin);
					StartClipboardCountdown();
					break;
				default:
					Debug.Assert(false);
					break;
			}
		}

		/// <summary>
		/// Do a quick find. All entries of the currently opened database are searched
		/// for a string and the results are automatically displayed in the main window.
		/// </summary>
		/// <param name="strSearch">String to search the entries for.</param>
		/// <param name="strGroupName">Group name of the group that receives the search
		/// results.</param>
		private void PerformQuickFind(string strSearch, string strGroupName)
		{
			Debug.Assert(strSearch != null); if(strSearch == null) return;
			Debug.Assert(strGroupName != null); if(strGroupName == null) return;

			PwGroup pg = new PwGroup(true, true, strGroupName, PwIcon.EMailSearch);
			pg.IsVirtual = true;

			SearchParameters sp = new SearchParameters();
			sp.SearchString = strSearch;
			sp.SearchInTitles = sp.SearchInUserNames = sp.SearchInPasswords =
				sp.SearchInUrls = sp.SearchInNotes = sp.SearchInOther = true;
			m_docMgr.ActiveDatabase.RootGroup.SearchEntries(sp, pg.Entries);

			UpdateEntryList(pg, false);
			SelectFirstEntryIfNoneSelected();

			UpdateUIState(false);
			ShowSearchResultsStatusMessage();
		}

		private void ShowExpiredEntries(bool bOnlyIfExists, uint uSkipDays)
		{
			PwGroup pg = new PwGroup(true, true, KPRes.ExpiredEntries, PwIcon.Expired);
			pg.IsVirtual = true;

			DateTime dtLimit = DateTime.Now;
			if(uSkipDays != 0)
				dtLimit = dtLimit.Add(new TimeSpan((int)uSkipDays, 0, 0, 0));

			EntryHandler eh = delegate(PwEntry pe)
			{
				if(PwDefs.IsTanEntry(pe)) return true; // Exclude TANs
				if(pe.Expires && (pe.ExpiryTime <= dtLimit))
					pg.Entries.Add(pe);
				return true;
			};

			m_docMgr.ActiveDatabase.RootGroup.TraverseTree(TraversalMethod.PreOrder, null, eh);

			if(uSkipDays != 0)
				pg.Name = KPRes.SoonToExpireEntries;

			if((pg.Entries.UCount > 1) || (bOnlyIfExists == false))
			{
				UpdateEntryList(pg, false);
				UpdateUIState(false);
			}
			else
			{
				UpdateEntryList(null, false);
				UpdateUIState(false);
			}

			ShowSearchResultsStatusMessage();
		}

		private void PerformExport(KeeExportFormat fmt)
		{
			string strFile = null;
			PerformExport(fmt, ref strFile);
		}

		/// <summary>
		/// Export the currently opened database to a file.
		/// </summary>
		/// <param name="fmt">Export format.</param>
		/// <param name="strToFile">File to export the data to. If this parameter is
		/// <c>null</c>, a dialog is displayed which prompts the user to specify a
		/// location. After the function returns, this parameter contains the path to
		/// which the user has really exported the data to (or it is <c>null</c>, if
		/// the export has been cancelled).</param>
		public void PerformExport(KeeExportFormat fmt, ref string strToFile)
		{
			Debug.Assert(m_docMgr.ActiveDatabase.IsOpen); if(!m_docMgr.ActiveDatabase.IsOpen) return;
			if(!AppPolicy.Try(AppPolicyID.Export)) return;

			if(fmt == KeeExportFormat.Kdb3)
			{
				Exception exLib;
				if(Kdb3File.IsLibraryInstalled(out exLib) == false)
				{
					MessageService.ShowWarning(KPRes.KeePassLibCNotFound,
						KPRes.KDB3KeePassLibC, exLib);
					return;
				}
			}

			System.Xml.Xsl.XslCompiledTransform xsl = null;
			if(fmt == KeeExportFormat.UseXsl)
			{
				GlobalWindowManager.AddDialog(m_openXslFile);
				DialogResult drXsl = m_openXslFile.ShowDialog();
				GlobalWindowManager.RemoveDialog(m_openXslFile);
				if(drXsl != DialogResult.OK) return;

				string strXslFile = m_openXslFile.FileName;

				xsl = new System.Xml.Xsl.XslCompiledTransform();

				try { xsl.Load(strXslFile); }
				catch(Exception exXsl)
				{
					MessageService.ShowWarning(strXslFile, KPRes.NoXSLFile, exXsl);
					return;
				}
			}

			string strSuggestion;
			if(m_docMgr.ActiveDatabase.IOConnectionInfo.Path.Length > 0)
				strSuggestion = UrlUtil.StripExtension(UrlUtil.GetFileName(
					m_docMgr.ActiveDatabase.IOConnectionInfo.Path));
			else strSuggestion = KPRes.Database;

			if(fmt == KeeExportFormat.PlainXml) strSuggestion += ".xml";
			else if(fmt == KeeExportFormat.Html) strSuggestion += ".html";
			else if(fmt == KeeExportFormat.Kdb3) strSuggestion += ".kdb";
			else if(fmt == KeeExportFormat.UseXsl) strSuggestion += ".*";
			else { Debug.Assert(false); }

			string strExt = UrlUtil.GetExtension(strSuggestion);
			string strPrevFilter = m_saveExportTo.Filter;
			m_saveExportTo.Filter = strExt.ToUpper() + " (*." + strExt + ")|*." +
				strExt + "|" + strPrevFilter;

			GlobalWindowManager.AddDialog(m_saveExportTo);
			m_saveExportTo.FileName = strSuggestion;
			if((strToFile != null) || (m_saveExportTo.ShowDialog() == DialogResult.OK))
			{
				this.Update();
				Application.DoEvents();

				string strTargetFile = (strToFile != null) ? strToFile : m_saveExportTo.FileName;
				strToFile = strTargetFile;

				ShowWarningsLogger swLogger = CreateShowWarningsLogger();
				swLogger.StartLogging(KPRes.ExportingStatusMsg, true);

				if(fmt == KeeExportFormat.PlainXml)
				{
					try
					{
						Kdb4File kdb = new Kdb4File(m_docMgr.ActiveDatabase);
						kdb.Save(strTargetFile, Kdb4Format.PlainXml, swLogger);
					}
					catch(Exception exPlain)
					{
						MessageService.ShowSaveWarning(strTargetFile, exPlain);
					}
				}
				else if(fmt == KeeExportFormat.Html)
				{
					PrintForm dlg = new PrintForm();
					dlg.InitEx(m_docMgr.ActiveDatabase.RootGroup, false);

					if(dlg.ShowDialog() == DialogResult.OK)
					{
						try
						{
							TextWriter tw = new StreamWriter(strTargetFile, false, Encoding.UTF8);
							tw.Write(dlg.GeneratedHTML);
							tw.Close();
						}
						catch(Exception twEx)
						{
							MessageService.ShowSaveWarning(strTargetFile, twEx);
						}
					}
				}
				else if(fmt == KeeExportFormat.Kdb3)
				{
					try
					{
						Kdb3File kdb = new Kdb3File(m_docMgr.ActiveDatabase, swLogger);
						kdb.Save(strTargetFile);
					}
					catch(Exception excpKdb3)
					{
						MessageService.ShowSaveWarning(strTargetFile, excpKdb3);
					}
				}
				else if(fmt == KeeExportFormat.UseXsl)
				{
					string strTempFile = strTargetFile + ".";
					strTempFile += Guid.NewGuid().ToString() + ".xml";

					try
					{
						Kdb4File kdb = new Kdb4File(m_docMgr.ActiveDatabase);
						kdb.Save(strTempFile, Kdb4Format.PlainXml, swLogger);
						xsl.Transform(strTempFile, strTargetFile);
					}
					catch(Exception exKdbXsl)
					{
						MessageService.ShowSaveWarning(strTempFile, exKdbXsl);
					}

					try { File.Delete(strTempFile); }
					catch(Exception) { }
				}
				else { Debug.Assert(false); }

				swLogger.EndLogging();
			}

			GlobalWindowManager.RemoveDialog(m_saveExportTo);

			m_saveExportTo.Filter = strPrevFilter;
			UpdateUIState(false);
		}

		/// <summary>
		/// Open a database. This function opens the specified database and updates
		/// the user interface.
		/// </summary>
		public void OpenDatabase(IOConnectionInfo ioConnection, CompositeKey cmpKey, bool bOpenLocal)
		{
			// OnFileClose(null, null);
			// if(m_docMgr.ActiveDatabase.IsOpen) return;

			if(m_bFormLoading && Program.Config.Application.Start.MinimizedAndLocked &&
				(ioConnection != null) && (ioConnection.Path.Length > 0))
			{
				DocumentStateEx ds = m_docMgr.CreateNewDocument(true);
				ds.LockedIoc = ioConnection.CloneDeep();
				UpdateUI(true, ds, true, null, true, null, false);
				return;
			}

			IOConnectionInfo ioc;
			if(ioConnection == null)
			{
				if(bOpenLocal)
				{
					GlobalWindowManager.AddDialog(m_openDatabaseFile);
					DialogResult dr = m_openDatabaseFile.ShowDialog();
					GlobalWindowManager.RemoveDialog(m_openDatabaseFile);
					if(dr != DialogResult.OK) return;

					ioc = IOConnectionInfo.FromPath(m_openDatabaseFile.FileName);
				}
				else
				{
					IOConnectionForm iocf = new IOConnectionForm();
					iocf.InitEx(false, new IOConnectionInfo());
					if(iocf.ShowDialog() != DialogResult.OK) return;

					ioc = iocf.IOConnectionInfo;
				}
			}
			else // ioConnection != null
			{
				ioc = ioConnection.CloneDeep();

				if((ioc.CredSaveMode != IOCredSaveMode.SaveCred) &&
					(!ioc.IsLocalFile()))
				{
					IOConnectionForm iocf = new IOConnectionForm();
					iocf.InitEx(false, ioc.CloneDeep());
					if(iocf.ShowDialog() != DialogResult.OK) return;

					ioc = iocf.IOConnectionInfo;
				}
			}

			if((ioc == null) || !ioc.CanProbablyAccess())
			{
				MessageService.ShowWarning(ioc.GetDisplayName(), KPRes.FileNotFoundError);
				return;
			}

			if(cmpKey == null)
			{
				for(int iTry = 0; iTry < 3; ++iTry)
				{
					KeyPromptForm kpf = new KeyPromptForm();
					kpf.InitEx(ioc.GetDisplayName(), IsFileLocked(null));

					DialogResult dr = kpf.ShowDialog();
					if(dr == DialogResult.Cancel) break;
					else if(kpf.HasClosedWithExit)
					{
						Debug.Assert(dr == DialogResult.Abort);
						OnFileExit(null, null);
						return;
					}

					if(OpenDatabaseInternal(ioc, kpf.CompositeKey))
						break;
				}
			}
			else // cmpKey != null
			{
				OpenDatabaseInternal(ioc, cmpKey);
			}

			if(m_docMgr.ActiveDatabase.IsOpen) // Opened successfully
			{
				string strName = m_docMgr.ActiveDatabase.IOConnectionInfo.GetDisplayName();
				m_mruList.AddItem(strName, m_docMgr.ActiveDatabase.IOConnectionInfo.CloneDeep());

				AutoEnableVisualHiding();

				if(Program.Config.Application.Start.OpenLastFile)
					Program.Config.Application.LastUsedFile =
						m_docMgr.ActiveDatabase.IOConnectionInfo.CloneDeep();
				else
					Program.Config.Application.LastUsedFile = new IOConnectionInfo();

				m_docMgr.ActiveDocument.LockedIoc = new IOConnectionInfo();

				if(this.FileOpened != null) this.FileOpened(this, EventArgs.Empty);
			}

			UpdateUI(true, null, true, null, true, null, false);
			UpdateColumnSortingIcons();

			if(m_docMgr.ActiveDatabase.IsOpen && Program.Config.Application.FileOpening.ShowSoonToExpireEntries)
			{
				ShowExpiredEntries(true, 7);

				// Avoid view being destroyed by the unlocking routine
				m_docMgr.ActiveDatabase.LastSelectedGroup = PwUuid.Zero;
			}
			else if(m_docMgr.ActiveDatabase.IsOpen && Program.Config.Application.FileOpening.ShowExpiredEntries)
			{
				ShowExpiredEntries(true, 0);

				// Avoid view being destroyed by the unlocking routine
				m_docMgr.ActiveDatabase.LastSelectedGroup = PwUuid.Zero;
			}

			ResetDefaultFocus(null);
		}

		private bool OpenDatabaseInternal(IOConnectionInfo ioc, CompositeKey cmpKey)
		{
			ShowWarningsLogger swLogger = CreateShowWarningsLogger();
			swLogger.StartLogging(KPRes.OpeningDatabase, true);

			DocumentStateEx ds = null;
			for(int iScan = 0; iScan < m_docMgr.Documents.Count; ++iScan)
			{
				if(m_docMgr.Documents[iScan].LockedIoc.Path == ioc.Path)
					ds = m_docMgr.Documents[iScan];
				else if(m_docMgr.Documents[iScan].Database.IOConnectionInfo.Path == ioc.Path)
					ds = m_docMgr.Documents[iScan];
			}

			PwDatabase pwDb;
			if(ds == null) pwDb = m_docMgr.CreateNewDocument(true).Database;
			else pwDb = ds.Database;

			bool bResult = true;
			try { pwDb.Open(ioc, cmpKey, swLogger); }
			catch(Exception ex)
			{
				MessageService.ShowLoadWarning(ioc.GetDisplayName(), ex);
				bResult = false;
			}

			swLogger.EndLogging();

			if(bResult == false)
			{
				if(ds == null) m_docMgr.CloseDatabase(m_docMgr.ActiveDatabase);
			}

			return bResult;
		}

		private void AutoEnableVisualHiding()
		{
			// KPF 1802197

			// Turn on visual hiding if option is selected
			// if(m_docMgr.ActiveDatabase.MemoryProtection.AutoEnableVisualHiding)
			// {
			//	if(m_docMgr.ActiveDatabase.MemoryProtection.ProtectTitle && !m_viewHideFields.ProtectTitle)
			//		m_menuViewHideTitles.Checked = m_viewHideFields.ProtectTitle = true;
			//	if(m_docMgr.ActiveDatabase.MemoryProtection.ProtectUserName && !m_viewHideFields.ProtectUserName)
			//		m_menuViewHideUserNames.Checked = m_viewHideFields.ProtectUserName = true;
			//	if(m_docMgr.ActiveDatabase.MemoryProtection.ProtectPassword && !m_viewHideFields.ProtectPassword)
			//		m_menuViewHidePasswords.Checked = m_viewHideFields.ProtectPassword = true;
			//	if(m_docMgr.ActiveDatabase.MemoryProtection.ProtectUrl && !m_viewHideFields.ProtectUrl)
			//		m_menuViewHideURLs.Checked = m_viewHideFields.ProtectUrl = true;
			//	if(m_docMgr.ActiveDatabase.MemoryProtection.ProtectNotes && !m_viewHideFields.ProtectNotes)
			//		m_menuViewHideNotes.Checked = m_viewHideFields.ProtectNotes = true;
			// }
		}

		private TreeNode GuiFindGroup(PwUuid puSearch, TreeNode tnContainer)
		{
			Debug.Assert(puSearch != null);
			if(puSearch == null) return null;

			if(m_tvGroups.Nodes.Count == 0) return null;
			
			if(tnContainer == null) tnContainer = m_tvGroups.Nodes[0];

			foreach(TreeNode tn in tnContainer.Nodes)
			{
				if(((PwGroup)tn.Tag).Uuid.EqualsValue(puSearch))
					return tn;

				if(tn != tnContainer)
				{
					TreeNode tnSub = GuiFindGroup(puSearch, tn);
					if(tnSub != null) return tnSub;
				}
				else { Debug.Assert(false); }
			}

			return null;
		}

		private ListViewItem GuiFindEntry(PwUuid puSearch)
		{
			Debug.Assert(puSearch != null);
			if(puSearch == null) return null;

			foreach(ListViewItem lvi in m_lvEntries.Items)
			{
				if(((PwEntry)lvi.Tag).Uuid.EqualsValue(puSearch))
					return lvi;
			}

			return null;
		}

		private static void PrintGroup(PwGroup pg)
		{
			Debug.Assert(pg != null); if(pg == null) return;
			if(!AppPolicy.Try(AppPolicyID.Print)) return;

			PrintForm pf = new PrintForm();
			pf.InitEx(pg, true);
			pf.ShowDialog();
		}

		private void ShowColumn(AppDefs.ColumnID colID, bool bShow)
		{
			m_vShowColumns[(int)colID] = bShow;

			if(bShow && (m_lvEntries.Columns[(int)colID].Width == 0))
			{
				m_lvEntries.Columns[(int)colID].Width = 100;
				RefreshEntriesList();
			}
			else if(!bShow && (m_lvEntries.Columns[(int)colID].Width != 0))
			{
				m_lvEntries.Columns[(int)colID].Width = 0;
			}

			switch(colID)
			{
				case AppDefs.ColumnID.Title: m_menuViewColumnsShowTitle.Checked = bShow; break;
				case AppDefs.ColumnID.UserName: m_menuViewColumnsShowUserName.Checked = bShow; break;
				case AppDefs.ColumnID.Password: m_menuViewColumnsShowPassword.Checked = bShow; break;
				case AppDefs.ColumnID.Url: m_menuViewColumnsShowUrl.Checked = bShow; break;
				case AppDefs.ColumnID.Notes: m_menuViewColumnsShowNotes.Checked = bShow; break;
				case AppDefs.ColumnID.CreationTime: m_menuViewColumnsShowCreation.Checked = bShow; break;
				case AppDefs.ColumnID.LastAccessTime: m_menuViewColumnsShowLastAccess.Checked = bShow; break;
				case AppDefs.ColumnID.LastModificationTime: m_menuViewColumnsShowLastMod.Checked = bShow; break;
				case AppDefs.ColumnID.ExpiryTime: m_menuViewColumnsShowExpire.Checked = bShow; break;
				case AppDefs.ColumnID.Uuid: m_menuViewColumnsShowUuid.Checked = bShow; break;
				case AppDefs.ColumnID.Attachment: m_menuViewColumnsShowAttachs.Checked = bShow; break;
				default: Debug.Assert(false); break;
			}
		}

		private void InsertToolStripItem(ToolStripMenuItem tsContainer, ToolStripMenuItem tsTemplate, EventHandler ev,
			bool bPermanentlyLinkToTemplate)
		{
			ToolStripMenuItem tsmi = new ToolStripMenuItem(tsTemplate.Text, tsTemplate.Image);
			tsmi.Click += ev;
			tsmi.ShortcutKeys = tsTemplate.ShortcutKeys;

			if(bPermanentlyLinkToTemplate)
				m_vLinkedToolStripItems.Add(new KeyValuePair<ToolStripItem, ToolStripItem>(
					tsTemplate, tsmi));

			tsContainer.DropDownItems.Insert(0, tsmi);
		}

		/// <summary>
		/// Set the linked menu item's Enabled state to the state of their parents.
		/// </summary>
		public void UpdateLinkedMenuItems()
		{
			foreach(KeyValuePair<ToolStripItem, ToolStripItem> kvp in m_vLinkedToolStripItems)
				kvp.Value.Enabled = kvp.Key.Enabled;
		}

		/// <summary>
		/// Handler function that is called when an MRU item is clicked (see MRU interface).
		/// </summary>
		public void OnMruExecute(string strDisplayName, object oTag)
		{
			OpenDatabase((IOConnectionInfo)oTag, null, false);
		}

		/// <summary>
		/// Handler function that is called when the MRU list must be cleared (see MRU interface).
		/// </summary>
		public void OnMruClear()
		{
			m_mruList.Clear();
			m_mruList.UpdateMenu();
		}

		/// <summary>
		/// Function to update the tray icon based on the current window state.
		/// </summary>
		public void UpdateTrayIcon()
		{
			bool bWindowVisible = this.Visible;
			bool bTrayVisible = m_ntfTray.Visible;

			if(Program.Config.UI.TrayIcon.ShowOnlyIfTrayed)
				m_ntfTray.Visible = !bWindowVisible;
			else if(bWindowVisible && !bTrayVisible)
				m_ntfTray.Visible = true;
		}

		private void OnSessionLock(object sender, EventArgs e)
		{
			if(m_docMgr.ActiveDatabase.IsOpen && !IsFileLocked(null))
			{
				if(Program.Config.Security.WorkspaceLocking.LockOnSessionLock)
					OnFileLock(sender, e);
			}

			// if((this.WindowState != FormWindowState.Minimized) &&
			//	(IsTrayed() == false) && (IsFileLocked(null) ||
			//	!m_docMgr.ActiveDatabase.IsOpen))
			// {
			//	if(Program.Config.MainWindow.MinimizeToTray) MinimizeToTray(true);
			//	else this.WindowState = FormWindowState.Minimized;
			// }
		}

		/// <summary>
		/// This function resets the internal user-inactivity timer.
		/// </summary>
		public void NotifyUserActivity()
		{
			m_nLockTimerCur = m_nLockTimerMax;
		}

		/// <summary>
		/// Move selected entries.
		/// </summary>
		/// <param name="nMove">Must be 2/-2 to move to top/bottom, 1/-1 to
		/// move one up/down.</param>
		private void MoveSelectedEntries(int nMove)
		{
			PwEntry[] vEntries = GetSelectedEntries();
			Debug.Assert(vEntries != null); if(vEntries == null) return;
			Debug.Assert(vEntries.Length > 0); if(vEntries.Length == 0) return;

			PwGroup pg = vEntries[0].ParentGroup;
			foreach(PwEntry pe in vEntries)
			{
				if(pe.ParentGroup != pg)
				{
					MessageService.ShowWarning(KPRes.CannotMoveEntriesBcsGroup);
					return;
				}
			}

			if(nMove == -1)
			{
				Debug.Assert(vEntries.Length == 1);
				pg.Entries.MoveOne(vEntries[0], false);
			}
			else if(nMove == 1)
			{
				Debug.Assert(vEntries.Length == 1);
				pg.Entries.MoveOne(vEntries[0], true);
			}
			else if(nMove == -2) pg.Entries.MoveTopBottom(vEntries, false);
			else if(nMove == 2) pg.Entries.MoveTopBottom(vEntries, true);

			UpdateEntryList(null, false);
			UpdateUIState(true);
		}

		/// <summary>
		/// Create a new warnings logger object that logs directly into
		/// the main status bar until the first warning is shown (in that
		/// case a dialog is opened displaying the warning).
		/// </summary>
		/// <returns>Reference to the new logger object.</returns>
		public ShowWarningsLogger CreateShowWarningsLogger()
		{
			StatusBarLogger sl = new StatusBarLogger();
			sl.SetControls(m_statusPartInfo, m_statusPartProgress);

			return new ShowWarningsLogger(sl);
		}

		/// <summary>
		/// Overridden <c>WndProc</c> to handle global hot keys.
		/// </summary>
		/// <param name="m">Reference to the current Windows message.</param>
		protected override void WndProc(ref Message m)
		{
			if(m.Msg == NativeMethods.WM_HOTKEY)
			{
				switch((int)m.WParam)
				{
					case AppDefs.GlobalHotKeyID.AutoType:
						if(m_bIsAutoTyping) break;
						m_bIsAutoTyping = true;

						if(IsAtLeastOneFileOpen() == false)
						{
							try
							{
								IntPtr hPrevWnd = NativeMethods.GetForegroundWindow();

								EnsureVisibleForegroundWindow();
								
								// The window restoration function above maybe
								// restored the window already, therefore only
								// try to unlock if it's locked *now*
								if(IsFileLocked(null)) OnFileLock(null, null);

								NativeMethods.EnsureForegroundWindow(hPrevWnd);
							}
							catch(Exception exAT)
							{
								MessageService.ShowWarning(exAT);
							}
						}
						if(!IsAtLeastOneFileOpen()) { m_bIsAutoTyping = false; break; }

						try
						{
							AutoType.PerformGlobal(m_docMgr.GetOpenDatabases(),
								m_ilCurrentIcons);
						}
						catch(Exception exGlobal)
						{
							MessageService.ShowWarning(exGlobal);
						}

						m_bIsAutoTyping = false;
						break;

					case AppDefs.GlobalHotKeyID.ShowWindow:
						bool bWndVisible = ((this.WindowState != FormWindowState.Minimized) &&
							!IsTrayed());
						EnsureVisibleForegroundWindow();
						if(bWndVisible && IsFileLocked(null))
							OnFileLock(null, EventArgs.Empty); // Unlock
						break;

					case AppDefs.GlobalHotKeyID.EntryMenu:
						EntryMenu.Show();
						break;

					default:
						Debug.Assert(false);
						break;
				}
			}
			else if((m.Msg == m_nAppMessage) && (m_nAppMessage != 0))
			{
				if(m.WParam == (IntPtr)Program.AppMessage.RestoreWindow)
					EnsureVisibleForegroundWindow();
				else if(m.WParam == (IntPtr)Program.AppMessage.Exit)
					this.OnFileExit(null, EventArgs.Empty);
			}
			else if(m.Msg == NativeMethods.WM_SYSCOMMAND)
			{
				if((m.WParam == (IntPtr)NativeMethods.SC_MINIMIZE) ||
					(m.WParam == (IntPtr)NativeMethods.SC_MAXIMIZE))
				{
					SaveWindowPositionAndSize();
				}
			}

			base.WndProc(ref m);
		}

		private void EnsureVisibleForegroundWindow()
		{
			if(IsTrayed()) MinimizeToTray(false);

			if(this.WindowState == FormWindowState.Minimized)
				this.WindowState = FormWindowState.Normal;

			this.BringToFront();
			this.Activate();
		}

		private void SetListFont(AceFont font)
		{
			if((font != null) && font.OverrideUIDefault)
			{
				m_tvGroups.Font = font.ToFont();
				m_lvEntries.Font = font.ToFont();
				m_richEntryView.Font = font.ToFont();

				Program.Config.UI.StandardFont = font;
			}
			else Program.Config.UI.StandardFont.OverrideUIDefault = false;

			m_fontExpired = new Font(m_tvGroups.Font, FontStyle.Strikeout);
		}

		private void SetSelectedEntryColor(Color clrBack)
		{
			if(m_docMgr.ActiveDatabase.IsOpen == false) return;
			PwEntry[] vSelected = GetSelectedEntries();
			if((vSelected == null) || (vSelected.Length == 0)) return;

			foreach(PwEntry pe in vSelected)
				pe.BackgroundColor = clrBack;

			RefreshEntriesList();
			UpdateUIState(true);
		}

		private void OnCopyCustomString(object sender, DynamicMenuEventArgs e)
		{
			PwEntry pe = GetSelectedEntry(false);
			if(pe == null) return;

			ClipboardUtil.CopyAndMinimize(pe.Strings.ReadSafe(e.ItemName), true,
				Program.Config.MainWindow.MinimizeAfterClipboardCopy ?
				this : null);
			StartClipboardCountdown();
		}

		private void SetMainWindowLayout(bool bSideBySide)
		{
			if(!bSideBySide && (m_splitHorizontal.Orientation != Orientation.Horizontal))
			{
				m_splitHorizontal.Orientation = Orientation.Horizontal;
				UpdateUIState(false);
			}
			else if(bSideBySide && (m_splitHorizontal.Orientation != Orientation.Vertical))
			{
				m_splitHorizontal.Orientation = Orientation.Vertical;
				UpdateUIState(false);
			}

			m_menuViewWindowsStacked.Checked = !bSideBySide;
			m_menuViewWindowsSideBySide.Checked = bSideBySide;
		}

		private static void UISelfTest()
		{
			Debug.Assert(((int)AppDefs.ColumnID.Title) == 0);
			Debug.Assert(((int)AppDefs.ColumnID.UserName) == 1);
		}

		private void AssignMenuShortcuts()
		{
			m_menuFileNew.ShortcutKeys = Keys.Control | Keys.N;
			m_menuFileOpenLocal.ShortcutKeys = Keys.Control | Keys.O;
			m_menuFileClose.ShortcutKeys = Keys.Control | Keys.W;
			m_menuFileSave.ShortcutKeys = Keys.Control | Keys.S;
			m_menuFilePrint.ShortcutKeys = Keys.Control | Keys.P;
			m_menuFileLock.ShortcutKeys = Keys.Control | Keys.L;

			m_menuEditFind.ShortcutKeys = Keys.Control | Keys.F;
			// m_ctxEntryAdd.ShortcutKeys = Keys.Control | Keys.N;

			m_menuHelpContents.ShortcutKeys = Keys.F1;
		}

		private void SaveDatabaseAs(bool bOnline, object sender, bool bCopy)
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return;
			if(!AppPolicy.Try(AppPolicyID.SaveFile)) return;

			if(FileSaving != null)
			{
				FileSavingEventArgs args = new FileSavingEventArgs(true, bCopy);
				FileSaving(sender, args);
				if(args.Cancel) return;
			}

			DialogResult dr;
			IOConnectionInfo ioc = new IOConnectionInfo();

			if(bOnline)
			{
				IOConnectionForm iocf = new IOConnectionForm();
				iocf.InitEx(true, m_docMgr.ActiveDatabase.IOConnectionInfo.CloneDeep());

				dr = iocf.ShowDialog();
				ioc = iocf.IOConnectionInfo;
			}
			else
			{
				m_saveDatabaseFile.FileName = UrlUtil.GetFileName(
					m_docMgr.ActiveDatabase.IOConnectionInfo.Path);

				GlobalWindowManager.AddDialog(m_saveDatabaseFile);
				dr = m_saveDatabaseFile.ShowDialog();
				GlobalWindowManager.RemoveDialog(m_saveDatabaseFile);

				if(dr == DialogResult.OK)
					ioc = IOConnectionInfo.FromPath(m_saveDatabaseFile.FileName);
			}

			if(dr == DialogResult.OK)
			{
				ShowWarningsLogger swLogger = CreateShowWarningsLogger();
				swLogger.StartLogging(KPRes.SavingDatabase, true);

				bool bSuccess = true;
				try
				{
					m_docMgr.ActiveDatabase.SaveAs(ioc, !bCopy, swLogger);

					if(bCopy == false)
					{
						string strName = ioc.GetDisplayName();
						m_mruList.AddItem(strName, ioc.CloneDeep());

						Program.Config.Application.LastUsedFile = ioc.CloneDeep();
					}
				}
				catch(Exception exSaveAs)
				{
					MessageService.ShowSaveWarning(ioc, exSaveAs);
					bSuccess = false;
				}

				swLogger.EndLogging();

				if(FileSaved != null)
				{
					FileSavedEventArgs args = new FileSavedEventArgs(bSuccess);
					FileSaved(sender, args);
				}
			}

			UpdateUIState(false);
		}

		public bool UIFileSave()
		{
			m_docMgr.ActiveDatabase.Modified = true;
			OnFileSave(null, null);
			return !m_docMgr.ActiveDatabase.Modified;
		}

		private void ResetDefaultFocus(Control cExplicit)
		{
			Control c = cExplicit;

			if(c == null)
			{
				if(m_tbQuickFind.Visible && m_tbQuickFind.Enabled)
					c = m_tbQuickFind.Control;
				else if(m_lvEntries.Visible && m_lvEntries.Enabled)
					c = m_lvEntries;
				else if(m_tvGroups.Visible && m_tvGroups.Enabled)
					c = m_tvGroups;
				else if(m_richEntryView.Visible && m_richEntryView.Enabled)
					c = m_richEntryView;
				else { Debug.Assert(false); c = m_lvEntries; }
			}

			try { this.ActiveControl = c; c.Focus(); }
			catch(Exception) { }
		}

		private static bool PrepareLock()
		{
			if(GlobalWindowManager.WindowCount == 0) return true;

			if(GlobalWindowManager.CanCloseAllWindows)
			{
				GlobalWindowManager.CloseAllWindows();
				return true;
			}

			return false;
		}

		private void UpdateImageLists()
		{
			if(!m_docMgr.ActiveDatabase.UINeedsIconUpdate) return;
			m_docMgr.ActiveDatabase.UINeedsIconUpdate = false;

			ImageList imgList = new ImageList();

			imgList.ImageSize = new Size(16, 16);
			imgList.ColorDepth = ColorDepth.Depth32Bit;

			foreach(Image img in m_ilClientIcons.Images)
				imgList.Images.Add(img);
			Debug.Assert(imgList.Images.Count == (int)PwIcon.Count);

			ImageList imgListCustom =
				UIUtil.BuildImageList(m_docMgr.ActiveDatabase.CustomIcons, 16, 16);

			foreach(Image imgCustom in imgListCustom.Images)
				imgList.Images.Add(imgCustom);

			m_ilCurrentIcons = imgList;

			ImageList imgFinal = UIUtil.ConvertImageList24(imgList, 16, 16,
				AppDefs.ColorControlNormal);
			m_tvGroups.ImageList = imgFinal;
			m_lvEntries.SmallImageList = imgFinal;
		}

		private void MoveSelectedGroup(int iMove)
		{
			PwGroup pgMove = GetSelectedGroup();
			if(pgMove == null) { Debug.Assert(false); return; }

			PwGroup pgParent = pgMove.ParentGroup;
			if(pgParent == null) return;

			PwGroup[] pgAffected = new PwGroup[]{ pgMove };

			if(iMove == 2)
				pgParent.Groups.MoveTopBottom(pgAffected, true);
			else if(iMove == 1)
				pgParent.Groups.MoveOne(pgMove, true);
			else if(iMove == -1)
				pgParent.Groups.MoveOne(pgMove, false);
			else if(iMove == -2)
				pgParent.Groups.MoveTopBottom(pgAffected, false);
			else { Debug.Assert(false); }

			UpdateUI(false, null, true, null, true, null, true);
		}

		private void OnEntryBinaryView(object sender, DynamicMenuEventArgs e)
		{
			PwEntry pe = GetSelectedEntry(false);
			if(pe == null) { Debug.Assert(false); return; }

			ProtectedBinary pbData = pe.Binaries.Get(e.ItemName);
			if(pbData == null) { Debug.Assert(false); return; }

			DataViewerForm dvf = new DataViewerForm();
			dvf.InitEx(e.ItemName, pbData.ReadData());

			dvf.ShowDialog();
		}

		private void SaveWindowState()
		{
			PwDatabase pd = m_docMgr.ActiveDatabase;

			PwGroup pgSelected = GetSelectedGroup();
			if(pgSelected != null)
				pd.LastSelectedGroup = new PwUuid(pgSelected.Uuid.UuidBytes);

			TreeNode tnTop = m_tvGroups.TopNode;
			if(tnTop != null)
			{
				PwGroup pgTop = tnTop.Tag as PwGroup;
				pd.LastTopVisibleGroup = new PwUuid(pgTop.Uuid.UuidBytes);
			}

			PwEntry peTop = GetTopEntry();
			if((peTop != null) && (pgSelected != null))
				pgSelected.LastTopVisibleEntry = new PwUuid(peTop.Uuid.UuidBytes);
		}

		private void RestoreWindowState(PwDatabase pd)
		{
			PwGroup pgSelect = null;

			if(pd.LastSelectedGroup != PwUuid.Zero)
			{
				pgSelect = pd.RootGroup.FindGroup(pd.LastSelectedGroup, true);
				UpdateGroupList(pgSelect);
				UpdateEntryList(pgSelect, false);
			}

			TreeNode tnTop = GuiFindGroup(pd.LastTopVisibleGroup, null);
			if(tnTop != null) m_tvGroups.TopNode = tnTop;

			if(pgSelect != null)
			{
				ListViewItem lviTop = GuiFindEntry(pgSelect.LastTopVisibleEntry);
				if(lviTop != null)
				{
					m_lvEntries.EnsureVisible(m_lvEntries.Items.Count - 1);
					m_lvEntries.EnsureVisible(lviTop.Index);
				}
			}
		}

		private void CloseActiveDocument(bool bLocking)
		{
			DocumentStateEx ds = m_docMgr.ActiveDocument;
			PwDatabase pd = ds.Database;

			// if(!pd.IsOpen) return;

			if(pd.Modified) // Implies pd.IsOpen
			{
				if(Program.Config.Application.FileClosing.AutoSave)
				{
					OnFileSave(null, EventArgs.Empty);
					if(pd.Modified) return;
				}
				else
				{
					string strMessage = pd.IOConnectionInfo.GetDisplayName();
					strMessage += MessageService.NewParagraph + KPRes.DatabaseModified +
						MessageService.NewParagraph + KPRes.SaveBeforeCloseQuestion;
					DialogResult dr = MessageService.Ask(strMessage,
						KPRes.SaveBeforeCloseTitle, MessageBoxButtons.YesNoCancel);

					if(dr == DialogResult.Cancel) return;
					else if(dr == DialogResult.Yes)
					{
						OnFileSave(null, EventArgs.Empty);
						if(pd.Modified) return;
					}
					else if(dr == DialogResult.No) { } // Changes are lost
				}
			}

			pd.Close();
			if(!bLocking) m_docMgr.CloseDatabase(pd);

			m_tbQuickFind.Items.Clear();
			m_tbQuickFind.Text = string.Empty;

			if(!bLocking) UpdateUI(true, null, true, null, true, null, false);

			if(FileClosed != null) FileClosed(null, EventArgs.Empty);
		}

		private void LockAllDocuments()
		{
			SaveWindowState();

			DocumentStateEx dsPrevActive = m_docMgr.ActiveDocument;

			foreach(DocumentStateEx ds in m_docMgr.Documents)
			{
				PwDatabase pd = ds.Database;

				if(!pd.IsOpen) continue; // Nothing to lock

				IOConnectionInfo ioIoc = pd.IOConnectionInfo;
				Debug.Assert(ioIoc != null);

				m_docMgr.ActiveDocument = ds;

				CloseActiveDocument(true);
				if(pd.IsOpen) return;

				ds.LockedIoc = ioIoc;
			}

			m_docMgr.ActiveDocument = dsPrevActive;
			UpdateUI(true, null, true, null, true, null, false);
		}

		private void SaveAllDocuments()
		{
			DocumentStateEx dsPrevActive = m_docMgr.ActiveDocument;

			foreach(DocumentStateEx ds in m_docMgr.Documents)
			{
				PwDatabase pd = ds.Database;

				if(!IsFileLocked(ds) && pd.Modified)
				{
					m_docMgr.ActiveDocument = ds;
					OnFileSave(null, EventArgs.Empty);
				}
			}

			m_docMgr.ActiveDocument = dsPrevActive;
			UpdateUI(false, null, true, null, true, null, false);
		}

		private bool CloseAllDocuments()
		{
			bool bProcessedAll = false, bSuccess = true;
			while(bProcessedAll == false)
			{
				bProcessedAll = true;

				foreach(DocumentStateEx ds in m_docMgr.Documents)
				{
					if(ds.Database.IsOpen)
					{
						m_docMgr.ActiveDocument = ds;
						CloseActiveDocument(false);

						if(ds.Database.IsOpen)
						{
							bSuccess = false;
							break;
						}

						bProcessedAll = false;
						break;
					}
				}

				if(bSuccess == false) break;
			}

			return bSuccess;
		}

		private void RecreateUITabs()
		{
			m_bBlockTabChanged = true;

			m_tabMain.TabPages.Clear();
			for(int i = 0; i < m_docMgr.Documents.Count; ++i)
			{
				DocumentStateEx ds = m_docMgr.Documents[i];

				TabPage tb = new TabPage();
				tb.Tag = ds;

				string strName, strTip;
				GetTabText(ds, out strName, out strTip);

				tb.Text = strName;
				tb.ToolTipText = strTip;

				m_tabMain.TabPages.Add(tb);
			}

			// m_tabMain.SelectedTab.Font = m_fontBoldUI;

			m_bBlockTabChanged = false;
		}

		private void SelectUITab()
		{
			m_bBlockTabChanged = true;

			DocumentStateEx dsSelect = m_docMgr.ActiveDocument;

			foreach(TabPage tb in m_tabMain.TabPages)
			{
				if((DocumentStateEx)tb.Tag == dsSelect)
				{
					m_tabMain.SelectedTab = tb;
					break;
				}
			}

			m_bBlockTabChanged = false;
		}

		private void GetTabText(DocumentStateEx dsInfo, out string strName,
			out string strTip)
		{
			if(IsFileLocked(dsInfo) == false) // Not locked
			{
				strTip = dsInfo.Database.IOConnectionInfo.Path;
				strName = UrlUtil.GetFileName(strTip);

				if(dsInfo.Database.Modified) strName += "*";

				if(dsInfo.Database.IsOpen)
				{
					if(dsInfo.Database.Name.Length > 0)
						strTip += "\r\n" + dsInfo.Database.Name;
				}
			}
			else // Locked
			{
				strTip = dsInfo.LockedIoc.Path;
				strName = UrlUtil.GetFileName(strTip);
				strName += " [" + KPRes.Locked + "]";
			}
		}

		private void UpdateUITabs()
		{
			foreach(TabPage tb in m_tabMain.TabPages)
			{
				DocumentStateEx ds = (DocumentStateEx)tb.Tag;
				string strName, strTip;

				GetTabText(ds, out strName, out strTip);

				tb.Text = strName;
				tb.ToolTipText = strTip;
			}
		}

		public void UpdateUI(bool bRecreateTabBar, DocumentStateEx dsSelect,
			bool bUpdateGroupList, PwGroup pgSelect, bool bUpdateEntryList,
			PwGroup pgEntrySource, bool bSetModified)
		{
			if(bRecreateTabBar) RecreateUITabs();

			if(dsSelect != null) m_docMgr.ActiveDocument = dsSelect;
			SelectUITab();

			UpdateImageLists();

			if(bUpdateGroupList) UpdateGroupList(pgSelect);
			if(bUpdateEntryList) UpdateEntryList(pgEntrySource, false);
			
			UpdateUIState(bSetModified);
		}

		private void ShowSearchResultsStatusMessage()
		{
			string strItemsFound = m_lvEntries.Items.Count.ToString() + " " +
				KPRes.SearchItemsFoundSmall;

			SetStatusEx(strItemsFound);
		}

		private void MinimizeToTray(bool bMinimize)
		{
			this.Visible = !bMinimize;

			if(bMinimize == false) // Restore
			{
				if(this.WindowState == FormWindowState.Minimized)
					this.WindowState = Program.Config.MainWindow.Maximized ?
						FormWindowState.Maximized : FormWindowState.Normal;
				else if(IsFileLocked(null))
					OnFileLock(null, EventArgs.Empty); // Unlock
			}

			UpdateTrayIcon();
		}

		private void MinimizeToTrayAtStartIfEnabled(bool bFormLoading)
		{
			if(Program.Config.Application.Start.MinimizedAndLocked)
			{
				if(bFormLoading) this.WindowState = FormWindowState.Minimized;

				if(Program.Config.MainWindow.MinimizeToTray) MinimizeToTray(true);
				else this.WindowState = FormWindowState.Minimized;
			}
		}

		private void SelectFirstEntryIfNoneSelected()
		{
			if((m_lvEntries.Items.Count > 0) &&
				(m_lvEntries.SelectedIndices.Count == 0))
			{
				m_lvEntries.Items[0].Selected = true;
				m_lvEntries.Items[0].Focused = true;
			}
		}

		private PwGroup GetCurrentEntries()
		{
			PwGroup pg = new PwGroup(true, true);
			pg.IsVirtual = true;

			foreach(ListViewItem lvi in m_lvEntries.Items)
				pg.Entries.Add(lvi.Tag as PwEntry);

			return pg;
		}
	}
}
