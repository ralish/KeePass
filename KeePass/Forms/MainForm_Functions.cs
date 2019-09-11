/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2009 Dominik Reichl <dominik.reichl@t-online.de>

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
using KeePass.DataExchange;
using KeePass.Ecas;
using KeePass.Native;
using KeePass.Plugins;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;

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
		private Font m_fontExpired = null;
		private Font m_fontBoldUI = null;
		private Font m_fontBoldTree = null;
		private Font m_fontItalicTree = null;
		private Point m_ptLastEntriesMouseClick = new Point(0, 0);
		private RichTextBoxContextMenu m_ctxEntryPreviewContextMenu = new RichTextBoxContextMenu();
		private DynamicMenu m_dynCustomStrings;
		private DynamicMenu m_dynCustomBinaries;

		private MemoryProtectionConfig m_viewHideFields = new MemoryProtectionConfig();

		private bool[] m_vShowColumns = new bool[(int)AppDefs.ColumnId.Count];
		private MruList m_mruList = new MruList();

		private SessionLockNotifier m_sessionLockNotifier = new SessionLockNotifier(true);

		private DefaultPluginHost m_pluginDefaultHost = new DefaultPluginHost();
		private PluginManager m_pluginManager = new PluginManager();

		private int m_nLockTimerMax = 0;
		private int m_nLockTimerCur = 0;
		private volatile bool m_bAllowLockTimerMod = true;
		private List<ToolStripButton> m_vCustomToolBarButtons = new List<ToolStripButton>();

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
		private bool m_bForceSave = false;
		private volatile uint m_uUIBlocked = 0;

		private int m_nAppMessage = Program.ApplicationMessage;

		private List<KeyValuePair<ToolStripItem, ToolStripItem>> m_vLinkedToolStripItems =
			new List<KeyValuePair<ToolStripItem, ToolStripItem>>();

		private FormWindowState m_fwsLast = FormWindowState.Normal;
		private PwGroup m_pgActiveAtDragStart = null;

		public DocumentManagerEx DocumentManager { get { return m_docMgr; } }
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

		public ContextMenuStrip EntryContextMenu { get { return m_ctxPwList; } }
		public ContextMenuStrip GroupContextMenu { get { return m_ctxGroupList; } }
		public ContextMenuStrip TrayContextMenu { get { return m_ctxTray; } }

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

		public bool IsFileLocked(PwDocument ds)
		{
			if(ds == null) ds = m_docMgr.ActiveDocument;

			return (ds.LockedIoc.Path.Length != 0);
		}

		public bool IsAtLeastOneFileOpen()
		{
			foreach(PwDocument ds in m_docMgr.Documents)
				if(ds.Database.IsOpen) return true;

			return false;
		}

		private void CleanUpEx()
		{
			Program.TriggerSystem.RaiseEvent(EcasEventIDs.AppExit);

			foreach(ToolStripButton tbCustom in m_vCustomToolBarButtons)
				tbCustom.Click -= OnCustomToolBarButtonClicked;
			m_vCustomToolBarButtons.Clear();

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

			Debug.Assert(m_uUIBlocked == 0);
			this.Visible = false;

			if(m_fontBoldUI != null) { m_fontBoldUI.Dispose(); m_fontBoldUI = null; }
			if(m_fontBoldTree != null) { m_fontBoldTree.Dispose(); m_fontBoldTree = null; }
			if(m_fontItalicTree != null) { m_fontItalicTree.Dispose(); m_fontItalicTree = null; }
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

			mw.ColumnsDict[PwDefs.TitleField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Title].Width;
			mw.ColumnsDict[PwDefs.UserNameField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.UserName].Width;
			mw.ColumnsDict[PwDefs.PasswordField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Password].Width;
			mw.ColumnsDict[PwDefs.UrlField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Url].Width;
			mw.ColumnsDict[PwDefs.NotesField].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Notes].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnCreationTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.CreationTime].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnLastAccessTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.LastAccessTime].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnLastModificationTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.LastModificationTime].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnExpiryTime].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.ExpiryTime].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnUuid].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Uuid].Width;
			mw.ColumnsDict[AppDefs.ColumnIdnAttachment].Width =
				m_lvEntries.Columns[(int)AppDefs.ColumnId.Attachment].Width;

			Debug.Assert(m_bSimpleTanView == m_menuViewTanSimpleList.Checked);
			mw.TanView.UseSimpleView = m_bSimpleTanView;
			Debug.Assert(m_bShowTanIndices == m_menuViewTanIndices.Checked);
			mw.TanView.ShowIndices = m_bShowTanIndices;

			mw.ColumnsDict[PwDefs.TitleField].HideWithAsterisks = m_menuViewHideTitles.Checked;
			mw.ColumnsDict[PwDefs.UserNameField].HideWithAsterisks = m_menuViewHideUserNames.Checked;
			mw.ColumnsDict[PwDefs.PasswordField].HideWithAsterisks = m_menuViewHidePasswords.Checked;
			mw.ColumnsDict[PwDefs.UrlField].HideWithAsterisks = m_menuViewHideURLs.Checked;
			mw.ColumnsDict[PwDefs.NotesField].HideWithAsterisks = m_menuViewHideNotes.Checked;

			SaveDisplayIndex(mw, PwDefs.TitleField, AppDefs.ColumnId.Title);
			SaveDisplayIndex(mw, PwDefs.UserNameField, AppDefs.ColumnId.UserName);
			SaveDisplayIndex(mw, PwDefs.PasswordField, AppDefs.ColumnId.Password);
			SaveDisplayIndex(mw, PwDefs.UrlField, AppDefs.ColumnId.Url);
			SaveDisplayIndex(mw, PwDefs.NotesField, AppDefs.ColumnId.Notes);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnAttachment, AppDefs.ColumnId.Attachment);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnCreationTime, AppDefs.ColumnId.CreationTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnExpiryTime, AppDefs.ColumnId.ExpiryTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnLastAccessTime, AppDefs.ColumnId.LastAccessTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnLastModificationTime, AppDefs.ColumnId.LastModificationTime);
			SaveDisplayIndex(mw, AppDefs.ColumnIdnUuid, AppDefs.ColumnId.Uuid);

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
			AppDefs.ColumnId colID)
		{
			mw.ColumnsDict[strColID].DisplayIndex = m_lvEntries.Columns[
				(int)colID].DisplayIndex;
		}

		private void RestoreDisplayIndex(AceMainWindow mw, string strColID,
			AppDefs.ColumnId colID)
		{
			try
			{
				int nIndex = mw.ColumnsDict[strColID].DisplayIndex;

				if((nIndex >= 0) && (nIndex < (int)AppDefs.ColumnId.Count))
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

			bool bEnableLockCmd = (bDatabaseOpened || IsFileLocked(null));
			bool bNoWindowShown = (GlobalWindowManager.WindowCount == 0);
			m_menuFileLock.Enabled = m_tbLockWorkspace.Enabled = bEnableLockCmd;
			m_ctxTrayTray.Enabled = bNoWindowShown;
			m_ctxTrayLock.Enabled = (bEnableLockCmd && bNoWindowShown);
			m_ctxTrayFileExit.Enabled = bNoWindowShown;

			m_menuEditFind.Enabled = m_menuToolsGeneratePwList.Enabled =
				m_menuToolsTanWizard.Enabled =
				m_menuEditShowAllEntries.Enabled = m_menuEditShowExpired.Enabled =
				m_menuToolsDbMaintenance.Enabled = bDatabaseOpened;

			m_menuFileImport.Enabled = m_menuFileExport.Enabled = bDatabaseOpened;

			m_menuFileSync.Enabled = m_menuFileSyncFile.Enabled =
				m_menuFileSyncUrl.Enabled = (bDatabaseOpened &&
				m_docMgr.ActiveDatabase.IOConnectionInfo.IsLocalFile() &&
				(m_docMgr.ActiveDatabase.IOConnectionInfo.Path.Length > 0));

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

			m_ctxEntryDuplicate.Enabled = m_ctxEntryMassSetIcon.Enabled =
				m_ctxEntrySelectedPrint.Enabled = m_ctxEntrySelectedExport.Enabled =
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

			m_tbCopyUserName.Enabled = m_ctxEntryCopyUserName.Enabled =
				((nEntriesSelected == 1) && (pe != null) &&
				(pe.Strings.GetSafe(PwDefs.UserNameField).Length > 0));
			m_tbCopyPassword.Enabled = m_ctxEntryCopyPassword.Enabled =
				((nEntriesSelected == 1) && (pe != null) &&
				(pe.Strings.GetSafe(PwDefs.PasswordField).Length > 0));
			m_ctxEntryCopyUrl.Enabled = m_ctxEntryUrlOpenInInternal.Enabled =
				((nEntriesSelected == 1) && (pe != null) &&
				(pe.Strings.GetSafe(PwDefs.UrlField).Length > 0));
			m_ctxEntryOpenUrl.Enabled = ((nEntriesSelected > 1) || ((pe != null) &&
				(pe.Strings.GetSafe(PwDefs.UrlField).Length > 0)));

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

			m_ctxEntrySaveAttachedFiles.Visible = m_ctxEntrySaveAttachedFiles.Enabled;

			bool bIsOneTan = (nEntriesSelected == 1);
			if(pe != null) bIsOneTan &= PwDefs.IsTanEntry(pe);
			else bIsOneTan = false;

			m_ctxEntryCopyUserName.Visible = !bIsOneTan;
			m_ctxEntryUrl.Visible = !bIsOneTan;
			m_ctxEntryCopyPassword.Text = (bIsOneTan ? KPRes.CopyTanMenu :
				KPRes.CopyPasswordMenu);

			string strLockUnlock = (IsFileLocked(null) ? KPRes.LockMenuUnlock :
				KPRes.LockMenuLock);
			m_menuFileLock.Text = strLockUnlock;
			m_tbLockWorkspace.Text = m_tbLockWorkspace.ToolTipText =
				m_ctxTrayLock.Text = StrUtil.RemoveAccelerator(strLockUnlock);

			m_tabMain.Visible = m_tbSaveAll.Visible = m_tbCloseTab.Visible =
				(m_docMgr.DocumentCount > 1);
			m_tbCloseTab.Enabled = bDatabaseOpened;

			bool bAtLeastOneModified = false;
			foreach(TabPage tabPage in m_tabMain.TabPages)
			{
				PwDocument dsPage = (PwDocument)tabPage.Tag;
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
		/// <returns>Matching entry or <c>null</c>.</returns>
		public PwEntry GetSelectedEntry(bool bRequireSelected)
		{
			return GetSelectedEntry(bRequireSelected, false);
		}

		public PwEntry GetSelectedEntry(bool bRequireSelected, bool bGetLastMatchingEntry)
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
				ListViewItem lvi = coll[bGetLastMatchingEntry ? (coll.Count - 1) : 0];
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
			for(int i = 0; i < coll.Count; ++i)
				vSelected[i] = (PwEntry)coll[i].Tag;

			return vSelected;
		}

		public uint GetSelectedEntriesCount()
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return 0;

			return (uint)m_lvEntries.SelectedIndices.Count;
		}

		public PwGroup GetSelectedEntriesAsGroup()
		{
			PwGroup pg = new PwGroup(true, true);

			PwGroup pgSel = GetSelectedGroup();
			if(pgSel != null)
			{
				pg.Name = pgSel.Name;
				pg.IconId = pgSel.IconId;
				pg.CustomIconUuid = pgSel.CustomIconUuid;
			}

			PwEntry[] vSel = GetSelectedEntries();
			if((vSel == null) || (vSel.Length == 0)) return pg;

			foreach(PwEntry pe in vSel) pg.AddEntry(pe, false);

			return pg;
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

		private ListViewItem AddEntryToList(PwEntry pe, ListViewStateEx lvse)
		{
			if((pe == null) || (lvse == null)) return null;

			ListViewItem lvi = new ListViewItem();
			lvi.Tag = pe;

			if(pe.Expires && (pe.ExpiryTime <= m_dtCachedNow))
			{
				lvi.ImageIndex = (int)PwIcon.Expired;
				if(m_fontExpired != null) lvi.Font = m_fontExpired;
			}
			else if(pe.CustomIconUuid == PwUuid.Zero)
				lvi.ImageIndex = (int)pe.IconId;
			else
				lvi.ImageIndex = (int)PwIcon.Count +
					m_docMgr.ActiveDatabase.GetCustomIconIndex(pe.CustomIconUuid);

			if(m_bEntryGrouping)
			{
				PwGroup pgContainer = pe.ParentGroup;
				PwGroup pgLast = ((m_lvgLastEntryGroup != null) ?
					(PwGroup)m_lvgLastEntryGroup.Tag : null);

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
			}
			else
			{
				if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Title] > 0)
				{
					if(m_viewHideFields.ProtectTitle) lvi.Text = PwDefs.HiddenPassword;
					else lvi.Text = pe.Strings.ReadSafe(PwDefs.TitleField);
				}
			}

			m_lvEntries.Items.Add(lvi);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.UserName] > 0)
			{
				if(m_viewHideFields.ProtectUserName) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.UserNameField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Password] > 0)
			{
				if(m_viewHideFields.ProtectPassword) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.PasswordField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Url] > 0)
			{
				if(m_viewHideFields.ProtectUrl) lvi.SubItems.Add(PwDefs.HiddenPassword);
				else lvi.SubItems.Add(pe.Strings.ReadSafe(PwDefs.UrlField));
			}
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Notes] > 0)
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

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.CreationTime] > 0)
				lvi.SubItems.Add(TimeUtil.ToDisplayString(pe.CreationTime));
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.LastAccessTime] > 0)
				lvi.SubItems.Add(TimeUtil.ToDisplayString(pe.LastAccessTime));
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.LastModificationTime] > 0)
				lvi.SubItems.Add(TimeUtil.ToDisplayString(pe.LastModificationTime));
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.ExpiryTime] > 0)
			{
				if(pe.Expires) lvi.SubItems.Add(TimeUtil.ToDisplayString(pe.ExpiryTime));
				else lvi.SubItems.Add(m_strNeverExpiresText);
			}
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Uuid] > 0)
				lvi.SubItems.Add(pe.Uuid.ToHexString());
			else lvi.SubItems.Add(string.Empty);

			if(lvse.ColumnWidths[(int)AppDefs.ColumnId.Attachment] > 0)
				lvi.SubItems.Add(pe.Binaries.UCount.ToString());
			else lvi.SubItems.Add(string.Empty);

			return lvi;
		}

		private void AddEntriesToList(PwObjectList<PwEntry> vEntries)
		{
			if(vEntries == null) { Debug.Assert(false); return; }

			m_bEntryGrouping = m_lvEntries.ShowGroups;

			ListViewStateEx lvseCachedState = new ListViewStateEx(m_lvEntries);
			foreach(PwEntry pe in vEntries)
			{
				if(pe == null) { Debug.Assert(false); continue; }

				if(m_bEntryGrouping)
				{
					PwGroup pg = pe.ParentGroup;

					foreach(ListViewGroup lvg in m_lvEntries.Groups)
					{
						PwGroup pgList = lvg.Tag as PwGroup;
						Debug.Assert(pgList != null);
						if((pgList != null) && (pg == pgList))
						{
							m_lvgLastEntryGroup = lvg;
							break;
						}
					}
				}

				AddEntryToList(pe, lvseCachedState);
			}

			Debug.Assert(lvseCachedState.CompareTo(m_lvEntries));
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
			PwGroup pg = (pgNewSelected ?? GetSelectedGroup());

			UpdateImageLists();

			m_tvGroups.BeginUpdate();
			m_tvGroups.Nodes.Clear();

			TreeNode tnRoot = null;
			if(pwDb.RootGroup != null)
			{
				tnRoot = new TreeNode(pwDb.RootGroup.Name, // + GetGroupSuffixText(pwDb.RootGroup),
					(int)pwDb.RootGroup.IconId, (int)pwDb.RootGroup.IconId);

				tnRoot.Tag = pwDb.RootGroup;
				if(m_fontBoldTree != null) tnRoot.NodeFont = m_fontBoldTree;
				UIUtil.SetGroupNodeToolTip(tnRoot, pwDb.RootGroup);
				
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

			PwGroup pg = (pgSelected ?? GetSelectedGroup());

			if(bOnlyUpdateCurrentlyShown)
			{
				Debug.Assert(pgSelected == null);
				pg = GetCurrentEntries();
			}

			PwObjectList<PwEntry> pwlSource = ((pg != null) ?
				pg.GetEntries(bSubEntries) : new PwObjectList<PwEntry>());

			m_lvEntries.BeginUpdate();
			m_lvEntries.Items.Clear();
			m_bOnlyTans = true;

			m_lvEntries.Groups.Clear();
			m_lvgLastEntryGroup = null;
			m_bEntryGrouping = (((pg != null) ? pg.IsVirtual : false) || bSubEntries);
			m_lvEntries.ShowGroups = m_bEntryGrouping;

			int nTopIndex = -1;
			ListViewItem lviFocused = null;

			m_dtCachedNow = DateTime.Now;
			ListViewStateEx lvseCachedState = new ListViewStateEx(m_lvEntries);

			if(pg != null)
			{
				foreach(PwEntry pe in pwlSource)
				{
					ListViewItem lvi = AddEntryToList(pe, lvseCachedState);

					if(vSelected != null)
					{
						if(Array.IndexOf(vSelected, pe) >= 0)
							lvi.Selected = true;
					}

					if(pe == peTop) nTopIndex = m_lvEntries.Items.Count - 1;
					if(pe == peFocused) lviFocused = lvi;
				}
			}

			Debug.Assert(lvseCachedState.CompareTo(m_lvEntries));

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
			if(vSelected == null) vSelected = new PwEntry[0];

			PwEntry[] vList = new PwEntry[nItemCount];
			for(int iEnum = 0; iEnum < nItemCount; ++iEnum)
				vList[iEnum] = (PwEntry)m_lvEntries.Items[iEnum].Tag;

			m_lvEntries.BeginUpdate();
			m_lvEntries.Items.Clear();

			m_lvEntries.Groups.Clear();
			m_lvgLastEntryGroup = null;

			int nTopIndex = -1;
			ListViewItem lviFocused = null;

			m_dtCachedNow = DateTime.Now;
			ListViewStateEx lvseCachedState = new ListViewStateEx(m_lvEntries);

			for(int iAdd = 0; iAdd < nItemCount; ++iAdd)
			{
				PwEntry pe = vList[iAdd];

				ListViewItem lvi = AddEntryToList(pe, lvseCachedState);

				if(pe == peTop) nTopIndex = iAdd;
				if(pe == peFocused) lviFocused = lvi;

				if(Array.IndexOf(vSelected, pe) >= 0)
					lvi.Selected = true;
			}

			Debug.Assert(lvseCachedState.CompareTo(m_lvEntries));

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

		private void RecursiveAddGroup(TreeNode tnParent, PwGroup pgContainer,
			PwGroup pgFind, ref TreeNode tnFound)
		{
			if(pgContainer == null) return;

			TreeNodeCollection tnc;
			if(tnParent == null) tnc = m_tvGroups.Nodes;
			else tnc = tnParent.Nodes;

			PwDatabase pd = m_docMgr.ActiveDatabase;
			foreach(PwGroup pg in pgContainer.Groups)
			{
				bool bExpired = false;
				if(pg.Expires && (pg.ExpiryTime <= m_dtCachedNow))
				{
					pg.IconId = PwIcon.Expired;
					bExpired = true;
				}

				string strName = pg.Name; // +GetGroupSuffixText(pg);

				int nIconID = ((pg.CustomIconUuid != PwUuid.Zero) ? ((int)PwIcon.Count +
					pd.GetCustomIconIndex(pg.CustomIconUuid)) : (int)pg.IconId);

				TreeNode tn = new TreeNode(strName, nIconID, nIconID);
				tn.Tag = pg;
				UIUtil.SetGroupNodeToolTip(tn, pg);

				if(pd.RecycleBinEnabled && pg.Uuid.EqualsValue(pd.RecycleBinUuid) &&
					(m_fontItalicTree != null))
					tn.NodeFont = m_fontItalicTree;
				else if(bExpired && (m_fontExpired != null))
					tn.NodeFont = m_fontExpired;

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
				bool bSortTimes = ((nColumn >= (int)AppDefs.ColumnId.CreationTime) &&
					(nColumn <= (int)AppDefs.ColumnId.ExpiryTime));

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
					m_pListSorter = new ListSorter(nColumn, sortOrder, bSortTimes);
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

			string strAsc = "  \u2191"; // Must have same length
			string strDsc = "  \u2193"; // Must have same length
			if(WinUtil.IsWindows9x || WinUtil.IsWindows2000 || WinUtil.IsWindowsXP)
			{
				strAsc = @"  ^";
				strDsc = @"  v";
			}
			else if(WinUtil.IsAtLeastWindowsVista)
			{
				strAsc = "  \u25B3";
				strDsc = "  \u25BD";
			}

			foreach(ColumnHeader ch in m_lvEntries.Columns)
			{
				string strCur = ch.Text, strNew = null;

				if(strCur.EndsWith(strAsc) || strCur.EndsWith(strDsc))
				{
					strNew = strCur.Substring(0, strCur.Length - strAsc.Length);
					strCur = strNew;
				}

				if((ch.Index == m_pListSorter.Column) &&
					(m_pListSorter.Order != SortOrder.None))
				{
					if(m_pListSorter.Order == SortOrder.Ascending)
						strNew = strCur + strAsc;
					else if(m_pListSorter.Order == SortOrder.Descending)
						strNew = strCur + strDsc;
				}

				if(strNew != null) ch.Text = strNew;
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

			int nGroupUrlStart = KPRes.Group.Length + 2;

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
			EvAppendEntryField(sb, strItemSeparator, KPRes.Url,
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

			if(pg != null)
			{
				m_richEntryView.Select(nGroupUrlStart, pg.Name.Length);
				UIUtil.RtfSetSelectionLink(m_richEntryView);
			}

			m_richEntryView.Select(0, 0);
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

		private void PerformDefaultAction(object sender, EventArgs e, PwEntry pe, AppDefs.ColumnId colID)
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
			bool bCnt = false;

			switch(colID)
			{
				case AppDefs.ColumnId.Title:
					if(PwDefs.IsTanEntry(pe))
						OnEntryCopyPassword(sender, e);
					else
						OnEntryEdit(sender, e);
					break;
				case AppDefs.ColumnId.UserName:
					OnEntryCopyUserName(sender, e);
					break;
				case AppDefs.ColumnId.Password:
					OnEntryCopyPassword(sender, e);
					break;
				case AppDefs.ColumnId.Url:
					OnEntryOpenUrl(sender, e);
					break;
				case AppDefs.ColumnId.Notes:
					bCnt = ClipboardUtil.CopyAndMinimize(pe.Strings.ReadSafe(
						PwDefs.NotesField), true, frmMin, pe, m_docMgr.ActiveDatabase);
					break;
				case AppDefs.ColumnId.CreationTime:
					bCnt = ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(
						pe.CreationTime), true, frmMin, pe, null);
					break;
				case AppDefs.ColumnId.LastAccessTime:
					bCnt = ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(
						pe.LastAccessTime), true, frmMin, pe, null);
					break;
				case AppDefs.ColumnId.LastModificationTime:
					bCnt = ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(
						pe.LastModificationTime), true, frmMin, pe, null);
					break;
				case AppDefs.ColumnId.ExpiryTime:
					if(pe.Expires)
						bCnt = ClipboardUtil.CopyAndMinimize(TimeUtil.ToDisplayString(
							pe.ExpiryTime), true, frmMin, pe, null);
					else
						bCnt = ClipboardUtil.CopyAndMinimize(KPRes.NeverExpires,
							true, frmMin, pe, null);
					break;
				case AppDefs.ColumnId.Attachment:
					break;
				case AppDefs.ColumnId.Uuid:
					bCnt = ClipboardUtil.CopyAndMinimize(pe.Uuid.ToHexString(),
						true, frmMin, pe, null);
					break;
				default:
					Debug.Assert(false);
					break;
			}

			if(bCnt) StartClipboardCountdown();
		}

		/// <summary>
		/// Do a quick find. All entries of the currently opened database are searched
		/// for a string and the results are automatically displayed in the main window.
		/// </summary>
		/// <param name="strSearch">String to search the entries for.</param>
		/// <param name="strGroupName">Group name of the group that receives the search
		/// results.</param>
		private void PerformQuickFind(string strSearch, string strGroupName,
			bool bForceShowExpired)
		{
			Debug.Assert(strSearch != null); if(strSearch == null) return;
			Debug.Assert(strGroupName != null); if(strGroupName == null) return;

			PwGroup pg = new PwGroup(true, true, strGroupName, PwIcon.EMailSearch);
			pg.IsVirtual = true;

			SearchParameters sp = new SearchParameters();
			sp.SearchString = strSearch;
			sp.SearchInTitles = sp.SearchInUserNames = sp.SearchInPasswords =
				sp.SearchInUrls = sp.SearchInNotes = sp.SearchInOther = true;

			if(bForceShowExpired == false)
				sp.ExcludeExpired = Program.Config.MainWindow.QuickFindExcludeExpired;

			m_docMgr.ActiveDatabase.RootGroup.SearchEntries(sp, pg.Entries);

			UpdateEntryList(pg, false);
			SelectFirstEntryIfNoneSelected();

			UpdateUIState(false);
			ShowSearchResultsStatusMessage();

			if(Program.Config.MainWindow.FocusResultsAfterQuickFind &&
				(pg.Entries.UCount > 0))
			{
				ResetDefaultFocus(m_lvEntries);
			}
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
					pg.AddEntry(pe, false);
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

		public void PerformExport(PwGroup pgDataSource, bool bExportDeleted)
		{
			Debug.Assert(m_docMgr.ActiveDatabase.IsOpen); if(!m_docMgr.ActiveDatabase.IsOpen) return;

			if(!AppPolicy.Try(AppPolicyId.Export)) return;

			PwDatabase pd = m_docMgr.ActiveDatabase;
			if((pd == null) || (pd.IsOpen == false)) return;

			PwGroup pg = (pgDataSource ?? pd.RootGroup);

			PwExportInfo pwInfo = new PwExportInfo(pg, pd, bExportDeleted);

			MessageService.ExternalIncrementMessageCount();

			ShowWarningsLogger swLogger = CreateShowWarningsLogger();
			swLogger.StartLogging(KPRes.ExportingStatusMsg, true);

			ExportUtil.Export(pwInfo, swLogger);

			swLogger.EndLogging();

			MessageService.ExternalDecrementMessageCount();
			UpdateUIState(false);
		}

		/// <summary>
		/// Open a database. This function opens the specified database and updates
		/// the user interface.
		/// </summary>
		public void OpenDatabase(IOConnectionInfo ioConnection, CompositeKey cmpKey,
			bool bOpenLocal)
		{
			// OnFileClose(null, null);
			// if(m_docMgr.ActiveDatabase.IsOpen) return;

			if(m_bFormLoading && Program.Config.Application.Start.MinimizedAndLocked &&
				(ioConnection != null) && (ioConnection.Path.Length > 0))
			{
				PwDocument ds = m_docMgr.CreateNewDocument(true);
				ds.LockedIoc = ioConnection.CloneDeep();
				UpdateUI(true, ds, true, null, true, null, false);
				return;
			}

			IOConnectionInfo ioc;
			if(ioConnection == null)
			{
				if(bOpenLocal)
				{
					OpenFileDialog ofdDb = UIUtil.CreateOpenFileDialog(KPRes.OpenDatabaseFile,
						UIUtil.CreateFileTypeFilter(AppDefs.FileExtension.FileExt,
						KPRes.KdbxFiles, true), 1, null, false, false);

					GlobalWindowManager.AddDialog(ofdDb);
					DialogResult dr = ofdDb.ShowDialog();
					GlobalWindowManager.RemoveDialog(ofdDb);
					if(dr != DialogResult.OK) return;

					ioc = IOConnectionInfo.FromPath(ofdDb.FileName);
				}
				else
				{
					IOConnectionForm iocf = new IOConnectionForm();
					iocf.InitEx(false, new IOConnectionInfo(), true, true);
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
					iocf.InitEx(false, ioc.CloneDeep(), true, true);
					if(iocf.ShowDialog() != DialogResult.OK) return;

					ioc = iocf.IOConnectionInfo;
				}
			}

			if((ioc == null) || !ioc.CanProbablyAccess())
			{
				MessageService.ShowWarning(ioc.GetDisplayName(), KPRes.FileNotFoundError);
				return;
			}

			if(OpenDatabaseRestoreIfOpened(ioc)) return;

			PwDatabase pwOpenedDb = null;
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

					pwOpenedDb = OpenDatabaseInternal(ioc, kpf.CompositeKey);
					if(pwOpenedDb != null) break;
				}
			}
			else // cmpKey != null
			{
				pwOpenedDb = OpenDatabaseInternal(ioc, cmpKey);
			}

			if((pwOpenedDb == null) || !pwOpenedDb.IsOpen) return;

			string strName = pwOpenedDb.IOConnectionInfo.GetDisplayName();
			m_mruList.AddItem(strName, pwOpenedDb.IOConnectionInfo.CloneDeep());

			PwDocument dsExisting = m_docMgr.FindDocument(pwOpenedDb);
			if(dsExisting != null) m_docMgr.ActiveDocument = dsExisting;

			bool bCorrectDbActive = (m_docMgr.ActiveDocument.Database == pwOpenedDb);
			Debug.Assert(bCorrectDbActive);

			AutoEnableVisualHiding();

			if(Program.Config.Application.Start.OpenLastFile)
				Program.Config.Application.LastUsedFile =
					pwOpenedDb.IOConnectionInfo.CloneDeep();
			else
				Program.Config.Application.LastUsedFile = new IOConnectionInfo();

			if(bCorrectDbActive)
				m_docMgr.ActiveDocument.LockedIoc = new IOConnectionInfo(); // Clear

			UpdateUI(true, null, true, null, true, null, false);
			UpdateColumnSortingIcons();

			if(this.FileOpened != null)
			{
				FileOpenedEventArgs ea = new FileOpenedEventArgs(pwOpenedDb);
				this.FileOpened(this, ea);
			}
			Program.TriggerSystem.RaiseEvent(EcasEventIDs.OpenedDatabaseFile,
				pwOpenedDb.IOConnectionInfo.Path);

			if(bCorrectDbActive && pwOpenedDb.IsOpen &&
				Program.Config.Application.FileOpening.ShowSoonToExpireEntries)
			{
				ShowExpiredEntries(true, 7);

				// Avoid view being destroyed by the unlocking routine
				pwOpenedDb.LastSelectedGroup = PwUuid.Zero;
			}
			else if(bCorrectDbActive && pwOpenedDb.IsOpen &&
				Program.Config.Application.FileOpening.ShowExpiredEntries)
			{
				ShowExpiredEntries(true, 0);

				// Avoid view being destroyed by the unlocking routine
				pwOpenedDb.LastSelectedGroup = PwUuid.Zero;
			}

			if(Program.Config.MainWindow.MinimizeAfterOpeningDatabase)
				this.WindowState = FormWindowState.Minimized;

			ResetDefaultFocus(null);
		}

		private PwDatabase OpenDatabaseInternal(IOConnectionInfo ioc, CompositeKey cmpKey)
		{
			ShowWarningsLogger swLogger = CreateShowWarningsLogger();
			swLogger.StartLogging(KPRes.OpeningDatabase, true);

			PwDocument ds = null;
			string strPathNrm = ioc.Path.Trim().ToLower();
			for(int iScan = 0; iScan < m_docMgr.Documents.Count; ++iScan)
			{
				if(m_docMgr.Documents[iScan].LockedIoc.Path.Trim().ToLower() == strPathNrm)
					ds = m_docMgr.Documents[iScan];
				else if(m_docMgr.Documents[iScan].Database.IOConnectionInfo.Path == strPathNrm)
					ds = m_docMgr.Documents[iScan];
			}

			PwDatabase pwDb;
			if(ds == null) pwDb = m_docMgr.CreateNewDocument(true).Database;
			else pwDb = ds.Database;

			try
			{
				pwDb.Open(ioc, cmpKey, swLogger);

#if DEBUG
				byte[] pbDiskDirect = WinUtil.HashFile(ioc);
				Debug.Assert(MemUtil.ArraysEqual(pbDiskDirect, pwDb.HashOfFileOnDisk));
#endif
			}
			catch(Exception ex)
			{
				MessageService.ShowLoadWarning(ioc.GetDisplayName(), ex);
				pwDb = null;
			}

			swLogger.EndLogging();

			if(pwDb == null)
			{
				if(ds == null) m_docMgr.CloseDatabase(m_docMgr.ActiveDatabase);
			}

			return pwDb;
		}

		private bool OpenDatabaseRestoreIfOpened(IOConnectionInfo ioc)
		{
			if(ioc == null) { Debug.Assert(false); return false; }

			string strPathNrm = ioc.Path.Trim().ToLower();

			foreach(PwDocument ds in m_docMgr.Documents)
			{
				if(((ds.LockedIoc == null) || (ds.LockedIoc.Path.Length == 0)) &&
					(ds.Database.IOConnectionInfo.Path.Trim().ToLower() == strPathNrm))
				{
					MakeDocumentActive(ds);
					return true;
				}
			}

			return false;
		}

		private static void AutoEnableVisualHiding() // Remove static when implementing
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
			if(!AppPolicy.Try(AppPolicyId.Print)) return;

			PrintForm pf = new PrintForm();
			pf.InitEx(pg, true);
			pf.ShowDialog();
		}

		private void ShowColumn(AppDefs.ColumnId colID, bool bShow)
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
				case AppDefs.ColumnId.Title: m_menuViewColumnsShowTitle.Checked = bShow; break;
				case AppDefs.ColumnId.UserName: m_menuViewColumnsShowUserName.Checked = bShow; break;
				case AppDefs.ColumnId.Password: m_menuViewColumnsShowPassword.Checked = bShow; break;
				case AppDefs.ColumnId.Url: m_menuViewColumnsShowUrl.Checked = bShow; break;
				case AppDefs.ColumnId.Notes: m_menuViewColumnsShowNotes.Checked = bShow; break;
				case AppDefs.ColumnId.CreationTime: m_menuViewColumnsShowCreation.Checked = bShow; break;
				case AppDefs.ColumnId.LastAccessTime: m_menuViewColumnsShowLastAccess.Checked = bShow; break;
				case AppDefs.ColumnId.LastModificationTime: m_menuViewColumnsShowLastMod.Checked = bShow; break;
				case AppDefs.ColumnId.ExpiryTime: m_menuViewColumnsShowExpire.Checked = bShow; break;
				case AppDefs.ColumnId.Uuid: m_menuViewColumnsShowUuid.Checked = bShow; break;
				case AppDefs.ColumnId.Attachment: m_menuViewColumnsShowAttachs.Checked = bShow; break;
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
					LockAllDocuments();
			}
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

		protected override void WndProc(ref Message m)
		{
			if(m.Msg == NativeMethods.WM_HOTKEY)
			{
				switch((int)m.WParam)
				{
					case AppDefs.GlobalHotKeyId.AutoType:
						ExecuteGlobalAutoType();
						break;

					case AppDefs.GlobalHotKeyId.ShowWindow:
						bool bWndVisible = ((this.WindowState != FormWindowState.Minimized) &&
							!IsTrayed());
						EnsureVisibleForegroundWindow(true, true);
						if(bWndVisible && IsFileLocked(null))
							OnFileLock(null, EventArgs.Empty); // Unlock
						break;

					case AppDefs.GlobalHotKeyId.EntryMenu:
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
					EnsureVisibleForegroundWindow(true, true);
				else if(m.WParam == (IntPtr)Program.AppMessage.Exit)
					this.OnFileExit(null, EventArgs.Empty);
				else if(m.WParam == (IntPtr)Program.AppMessage.IpcByFile)
					IpcUtilEx.ProcessGlobalMessage(m.LParam.ToInt32(), this);
			}
			else if(m.Msg == NativeMethods.WM_SYSCOMMAND)
			{
				if((m.WParam == (IntPtr)NativeMethods.SC_MINIMIZE) ||
					(m.WParam == (IntPtr)NativeMethods.SC_MAXIMIZE))
				{
					SaveWindowPositionAndSize();
				}
			}
			else if((m.Msg == NativeMethods.WM_POWERBROADCAST) &&
				((m.WParam == (IntPtr)NativeMethods.PBT_APMQUERYSUSPEND) ||
				(m.WParam == (IntPtr)NativeMethods.PBT_APMSUSPEND)))
			{
				if(Program.Config.Security.WorkspaceLocking.LockOnSessionLock)
					LockAllDocuments();
			}

			base.WndProc(ref m);
		}

		public void ExecuteGlobalAutoType()
		{
			if(m_bIsAutoTyping) return;
			m_bIsAutoTyping = true;

			if(IsAtLeastOneFileOpen() == false)
			{
				try
				{
					IntPtr hPrevWnd = NativeMethods.GetForegroundWindow();

					EnsureVisibleForegroundWindow(false, false);

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
			if(!IsAtLeastOneFileOpen()) { m_bIsAutoTyping = false; return; }

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
		}

		public void EnsureVisibleForegroundWindow(bool bUntray, bool bRestoreWindow)
		{
			if(bUntray && IsTrayed()) MinimizeToTray(false);

			if(bRestoreWindow && (this.WindowState == FormWindowState.Minimized))
				this.WindowState = FormWindowState.Normal;

			try
			{
				this.BringToFront();
				this.Activate();
			}
			catch(Exception) { Debug.Assert(false); }
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

			if(m_fontExpired == null)
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

			if(ClipboardUtil.CopyAndMinimize(pe.Strings.ReadSafe(e.ItemName), true,
				Program.Config.MainWindow.MinimizeAfterClipboardCopy ?
				this : null, pe, m_docMgr.ActiveDatabase))
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
			Debug.Assert(((int)AppDefs.ColumnId.Title) == 0);
			Debug.Assert(((int)AppDefs.ColumnId.UserName) == 1);
		}

		private void AssignMenuShortcuts()
		{
			m_menuFileNew.ShortcutKeys = (Keys.Control | Keys.N);
			m_menuFileOpenLocal.ShortcutKeys = (Keys.Control | Keys.O);
			m_menuFileClose.ShortcutKeys = (Keys.Control | Keys.W);
			m_menuFileSave.ShortcutKeys = (Keys.Control | Keys.S);
			m_menuFilePrint.ShortcutKeys = (Keys.Control | Keys.P);
			m_menuFileLock.ShortcutKeys = (Keys.Control | Keys.L);

			m_menuEditFind.ShortcutKeys = (Keys.Control | Keys.F);
			// m_ctxEntryAdd.ShortcutKeys = Keys.Control | Keys.N;

			m_menuHelpContents.ShortcutKeys = Keys.F1;
		}

		private void SaveDatabaseAs(bool bOnline, object sender, bool bCopy)
		{
			if(!m_docMgr.ActiveDatabase.IsOpen) return;
			if(!AppPolicy.Try(AppPolicyId.SaveFile)) return;

			PwDatabase pd = m_docMgr.ActiveDatabase;

			Guid eventGuid = Guid.NewGuid();
			if(this.FileSaving != null)
			{
				FileSavingEventArgs args = new FileSavingEventArgs(true, bCopy, pd, eventGuid);
				this.FileSaving(sender, args);
				if(args.Cancel) return;
			}

			DialogResult dr;
			IOConnectionInfo ioc = new IOConnectionInfo();

			if(bOnline)
			{
				IOConnectionForm iocf = new IOConnectionForm();
				iocf.InitEx(true, pd.IOConnectionInfo.CloneDeep(), true, true);

				dr = iocf.ShowDialog();
				ioc = iocf.IOConnectionInfo;
			}
			else
			{
				SaveFileDialog sfdDb = UIUtil.CreateSaveFileDialog(KPRes.SaveDatabase,
					UrlUtil.GetFileName(pd.IOConnectionInfo.Path),
					UIUtil.CreateFileTypeFilter(AppDefs.FileExtension.FileExt,
					KPRes.KdbxFiles, true), 1, AppDefs.FileExtension.FileExt, false);

				GlobalWindowManager.AddDialog(sfdDb);
				dr = sfdDb.ShowDialog();
				GlobalWindowManager.RemoveDialog(sfdDb);

				if(dr == DialogResult.OK)
					ioc = IOConnectionInfo.FromPath(sfdDb.FileName);
			}

			if(dr == DialogResult.OK)
			{
				Program.TriggerSystem.RaiseEvent(EcasEventIDs.SavingDatabaseFile,
					ioc.Path);

				UIBlockInteraction(true);

				ShowWarningsLogger swLogger = CreateShowWarningsLogger();
				swLogger.StartLogging(KPRes.SavingDatabase, true);

				bool bSuccess = true;
				try
				{
					pd.SaveAs(ioc, !bCopy, swLogger);

					PostSavingEx(!bCopy, pd, ioc);
				}
				catch(Exception exSaveAs)
				{
					MessageService.ShowSaveWarning(ioc, exSaveAs, true);
					bSuccess = false;
				}

				swLogger.EndLogging();

				// Immediately after the UIBlockInteraction call the form might
				// be closed and UpdateUIState might crash, if the order of the
				// two methods is swapped; so first update state, then unblock
				UpdateUIState(false);
				UIBlockInteraction(false); // Calls Application.DoEvents()

				if(this.FileSaved != null)
				{
					FileSavedEventArgs args = new FileSavedEventArgs(bSuccess, pd, eventGuid);
					this.FileSaved(sender, args);
				}
				if(bSuccess)
					Program.TriggerSystem.RaiseEvent(EcasEventIDs.SavedDatabaseFile,
						ioc.Path);
			}
		}

		private void PostSavingEx(bool bPrimary, PwDatabase pwDatabase, IOConnectionInfo ioc)
		{
			if(ioc == null) { Debug.Assert(false); return; }

			byte[] pbIO = WinUtil.HashFile(ioc);
			Debug.Assert((pbIO != null) && (pwDatabase.HashOfLastIO != null));
			if(pwDatabase.HashOfLastIO != null)
			{
				if(!MemUtil.ArraysEqual(pbIO, pwDatabase.HashOfLastIO))
				{
					MessageService.ShowWarning(ioc.GetDisplayName(), KPRes.FileVerifyHashFail,
						KPRes.FileVerifyHashFailRec);
				}
			}

			if(bPrimary)
			{
#if DEBUG
				Debug.Assert(MemUtil.ArraysEqual(pbIO, pwDatabase.HashOfFileOnDisk));

				try
				{
					PwDatabase pwCheck = new PwDatabase();
					pwCheck.Open(ioc.CloneDeep(), pwDatabase.MasterKey, null);

					Debug.Assert(MemUtil.ArraysEqual(pwDatabase.HashOfLastIO,
						pwCheck.HashOfLastIO));

					uint uGroups1, uGroups2, uEntries1, uEntries2;
					pwDatabase.RootGroup.GetCounts(true, out uGroups1, out uEntries1);
					pwCheck.RootGroup.GetCounts(true, out uGroups2, out uEntries2);
					Debug.Assert((uGroups1 == uGroups2) && (uEntries1 == uEntries2));
				}
				catch(Exception exVerify) { Debug.Assert(false, exVerify.Message); }
#endif

				m_mruList.AddItem(ioc.GetDisplayName(), ioc.CloneDeep());

				Program.Config.Application.LastUsedFile = ioc.CloneDeep();
			}

			WinUtil.FlushStorageBuffers(ioc.Path, true);
		}

		public bool UIFileSave(bool bForceSave)
		{
			m_docMgr.ActiveDatabase.Modified = true;

			m_bForceSave = bForceSave;
			OnFileSave(null, null);
			m_bForceSave = false;

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
				else c = m_lvEntries;
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

			ImageList imgListCustom = UIUtil.BuildImageList(
				m_docMgr.ActiveDatabase.CustomIcons, 16, 16);

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

		private void CloseActiveDocument(bool bLocking, bool bExiting)
		{
			PwDocument ds = m_docMgr.ActiveDocument;
			PwDatabase pd = ds.Database;
			IOConnectionInfo ioClosing = pd.IOConnectionInfo.CloneDeep();

			if(pd.Modified) // Implies pd.IsOpen
			{
				if(Program.Config.Application.FileClosing.AutoSave)
				{
					OnFileSave(null, EventArgs.Empty);
					if(pd.Modified) return;
				}
				else
				{
					FileSaveOrigin fso = FileSaveOrigin.Closing;
					if(bLocking) fso = FileSaveOrigin.Locking;
					if(bExiting) fso = FileSaveOrigin.Exiting;

					DialogResult dr = FileDialogsEx.ShowFileSaveQuestion(
						pd.IOConnectionInfo.GetDisplayName(), fso, this.Handle);

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

			if(!bLocking)
			{
				m_docMgr.ActiveDatabase.UINeedsIconUpdate = true;
				UpdateUI(true, null, true, null, true, null, false);
			}

			if(FileClosed != null)
			{
				FileClosedEventArgs fcea = new FileClosedEventArgs(ioClosing);
				FileClosed(null, fcea);
			}
		}

		private void LockAllDocuments()
		{
			if(UIIsInteractionBlocked()) { Debug.Assert(false); return; }

			SaveWindowState();

			PwDocument dsPrevActive = m_docMgr.ActiveDocument;

			foreach(PwDocument ds in m_docMgr.Documents)
			{
				PwDatabase pd = ds.Database;

				if(!pd.IsOpen) continue; // Nothing to lock

				IOConnectionInfo ioIoc = pd.IOConnectionInfo;
				Debug.Assert(ioIoc != null);

				m_docMgr.ActiveDocument = ds;

				CloseActiveDocument(true, false);
				if(pd.IsOpen) return;

				ds.LockedIoc = ioIoc;
			}

			m_docMgr.ActiveDocument = dsPrevActive;
			UpdateUI(true, null, true, null, true, null, false);

			if(Program.Config.MainWindow.MinimizeAfterLocking &&
				!IsAtLeastOneFileOpen())
				this.WindowState = FormWindowState.Minimized;
		}

		private void SaveAllDocuments()
		{
			PwDocument dsPrevActive = m_docMgr.ActiveDocument;

			foreach(PwDocument ds in m_docMgr.Documents)
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

		private bool CloseAllDocuments(bool bExiting)
		{
			if(UIIsInteractionBlocked()) { Debug.Assert(false); return false; }

			bool bProcessedAll = false, bSuccess = true;
			while(bProcessedAll == false)
			{
				bProcessedAll = true;

				foreach(PwDocument ds in m_docMgr.Documents)
				{
					if(ds.Database.IsOpen)
					{
						m_docMgr.ActiveDocument = ds;
						CloseActiveDocument(false, bExiting);

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
				PwDocument ds = m_docMgr.Documents[i];

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

			PwDocument dsSelect = m_docMgr.ActiveDocument;

			foreach(TabPage tb in m_tabMain.TabPages)
			{
				if((PwDocument)tb.Tag == dsSelect)
				{
					m_tabMain.SelectedTab = tb;
					break;
				}
			}

			m_bBlockTabChanged = false;
		}

		private void MakeDocumentActive(PwDocument ds)
		{
			if(ds == null) { Debug.Assert(false); return; }

			ds.Database.UINeedsIconUpdate = true;

			UpdateUI(false, ds, true, null, true, null, false);

			RestoreWindowState(ds.Database);
			UpdateUIState(false);
		}

		private void GetTabText(PwDocument dsInfo, out string strName,
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
				PwDocument ds = (PwDocument)tb.Tag;
				string strName, strTip;

				GetTabText(ds, out strName, out strTip);

				tb.Text = strName;
				tb.ToolTipText = strTip;
			}
		}

		public void UpdateUI(bool bRecreateTabBar, PwDocument dsSelect,
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
					this.WindowState = (Program.Config.MainWindow.Maximized ?
						FormWindowState.Maximized : FormWindowState.Normal);
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

		private void SelectEntries(PwObjectList<PwEntry> lEntries, bool bDeselectOthers)
		{
			m_bBlockEntrySelectionEvent = true;

			for(int i = 0; i < m_lvEntries.Items.Count; ++i)
			{
				PwEntry pe = m_lvEntries.Items[i].Tag as PwEntry;
				if(pe == null) { Debug.Assert(false); continue; }

				bool bFound = false;
				foreach(PwEntry peFocus in lEntries)
				{
					if(pe == peFocus)
					{
						m_lvEntries.Items[i].Selected = true;
						bFound = true;
						break;
					}
				}

				if(bDeselectOthers && !bFound)
					m_lvEntries.Items[i].Selected = false;
			}

			m_bBlockEntrySelectionEvent = false;
		}

		private PwGroup GetCurrentEntries()
		{
			PwGroup pg = new PwGroup(true, true);
			pg.IsVirtual = true;

			if(m_lvEntries.ShowGroups == false)
			{
				foreach(ListViewItem lvi in m_lvEntries.Items)
					pg.AddEntry(lvi.Tag as PwEntry, false);
			}
			else // Groups
			{
				foreach(ListViewGroup lvg in m_lvEntries.Groups)
					foreach(ListViewItem lvi in lvg.Items)
						pg.AddEntry(lvi.Tag as PwEntry, false);
			}

			return pg;
		}

		private void EnsureVisibleEntry(PwUuid uuid)
		{
			ListViewItem lvi = GuiFindEntry(uuid);
			if(lvi == null) { Debug.Assert(false); return; }

			m_lvEntries.EnsureVisible(lvi.Index);
		}

		private void EnsureVisibleSelected(bool bLastMatchingEntry)
		{
			PwEntry pe = GetSelectedEntry(true, bLastMatchingEntry);
			if(pe == null) return;

			EnsureVisibleEntry(pe.Uuid);
		}

		private void RemoveEntriesFromList(List<PwEntry> lEntries, bool bLockUIUpdate)
		{
			Debug.Assert(lEntries != null); if(lEntries == null) return;
			if(lEntries.Count == 0) return;

			RemoveEntriesFromList(lEntries.ToArray(), bLockUIUpdate);
		}

		private void RemoveEntriesFromList(PwEntry[] vEntries, bool bLockUIUpdate)
		{
			Debug.Assert(vEntries != null); if(vEntries == null) return;
			if(vEntries.Length == 0) return;

			if(bLockUIUpdate) m_lvEntries.BeginUpdate();

			for(int i = m_lvEntries.Items.Count - 1; i >= 0; --i)
			{
				PwEntry pe = m_lvEntries.Items[i].Tag as PwEntry;
				Debug.Assert(pe != null);

				if(Array.IndexOf<PwEntry>(vEntries, pe) >= 0)
					m_lvEntries.Items.RemoveAt(i);
			}

			if(bLockUIUpdate) m_lvEntries.EndUpdate();
		}

		private static void ConfigureTbButton(ToolStripItem tb, string strText,
			string strTooltip)
		{
			if(strText != null) tb.Text = strText;

			if(strTooltip != null)
				tb.ToolTipText = StrUtil.RemoveAccelerator(strTooltip);
			else if(strText != null)
				tb.ToolTipText = StrUtil.RemoveAccelerator(strText);
		}

		private bool PreSaveValidate(PwDatabase pd)
		{
			if(m_bForceSave) return true;

			byte[] pbOnDisk = WinUtil.HashFile(pd.IOConnectionInfo);

			if((pbOnDisk != null) && (pd.HashOfFileOnDisk != null) &&
				!MemUtil.ArraysEqual(pbOnDisk, pd.HashOfFileOnDisk))
			{
				DialogResult dr = AskIfSynchronizeInstead(pd.IOConnectionInfo);
				if(dr == DialogResult.Yes) // Synchronize
				{
					bool b = ImportUtil.Synchronize(pd, this, pd.IOConnectionInfo, true);
					UpdateUI(false, null, true, null, true, null, false);
					SetStatusEx(b ? KPRes.SyncSuccess : KPRes.SyncFailed);
					return false;
				}
				else if(dr == DialogResult.Cancel) return false;
				else { Debug.Assert(dr == DialogResult.No); }
			}

			return true;
		}

		private DialogResult AskIfSynchronizeInstead(IOConnectionInfo ioc)
		{
			VistaTaskDialog dlg = new VistaTaskDialog(this.Handle);

			string strText = string.Empty;
			if(ioc.GetDisplayName().Length > 0)
				strText += ioc.GetDisplayName() + MessageService.NewParagraph;
			strText += KPRes.FileChanged;

			dlg.CommandLinks = true;
			dlg.WindowTitle = PwDefs.ProductName;
			dlg.Content = strText;
			dlg.SetIcon(VtdCustomIcon.Question);

			dlg.MainInstruction = KPRes.OverwriteExistingFileQuestion;
			dlg.AddButton((int)DialogResult.Yes, KPRes.Synchronize, KPRes.FileChangedSync);
			dlg.AddButton((int)DialogResult.No, KPRes.Overwrite, KPRes.FileChangedOverwrite);
			dlg.AddButton((int)DialogResult.Cancel, KPRes.Cancel, KPRes.FileSaveQOpCancel);

			DialogResult dr;
			if(dlg.ShowDialog()) dr = (DialogResult)dlg.Result;
			else
			{
				strText += MessageService.NewParagraph;
				strText += @"[" + KPRes.Yes + @"]: " + KPRes.Synchronize + @". " +
					KPRes.FileChangedSync + MessageService.NewParagraph;
				strText += @"[" + KPRes.No + @"]: " + KPRes.Overwrite + @". " +
					KPRes.FileChangedOverwrite + MessageService.NewParagraph;
				strText += @"[" + KPRes.Cancel + @"]: " + KPRes.FileSaveQOpCancel;

				dr = MessageService.Ask(strText, PwDefs.ShortProductName,
					MessageBoxButtons.YesNoCancel);
			}

			return dr;
		}

		private void ActivateNextDocumentEx()
		{
			if(m_tabMain.TabPages.Count > 1)
				m_tabMain.SelectedIndex = ((m_tabMain.SelectedIndex + 1) %
					m_tabMain.TabPages.Count);
		}

		private bool HandleMainWindowKeyMessage(KeyEventArgs e, bool bDown)
		{
			if(e == null) { Debug.Assert(false); return false; }

			bool bHandled = false;

			if(e.Control)
			{
				if(e.KeyCode == Keys.Tab)
				{
					if(bDown) ActivateNextDocumentEx();

					bHandled = true;
				}
			}

			if(bHandled) e.Handled = true;
			return bHandled;
		}

		private bool UIIsInteractionBlocked()
		{
			return (m_uUIBlocked > 0);
		}

		private void UIBlockInteraction(bool bBlock)
		{
			NotifyUserActivity();

			if(bBlock) ++m_uUIBlocked;
			else if(m_uUIBlocked > 0) --m_uUIBlocked;
			else { Debug.Assert(false); }

			bool bNotBlocked = !UIIsInteractionBlocked();
			this.Enabled = bNotBlocked;

			if(bNotBlocked)
			{
				try { ResetDefaultFocus(null); } // Set focus on unblock
				catch(Exception) { Debug.Assert(false); }
			}

			Application.DoEvents(); // Allow controls update/redraw
		}

		private static void EnsureRecycleBin(ref PwGroup pgRecycleBin,
			PwDatabase pdContext, ref bool bGroupListUpdateRequired)
		{
			if(pdContext == null) { Debug.Assert(false); return; }

			if(pgRecycleBin == pdContext.RootGroup)
			{
				Debug.Assert(false);
				pgRecycleBin = null;
			}

			if(pgRecycleBin == null)
			{
				pgRecycleBin = new PwGroup(true, true, KPRes.RecycleBin,
					PwIcon.TrashBin);
				pdContext.RootGroup.AddGroup(pgRecycleBin, true);

				pdContext.RecycleBinUuid = pgRecycleBin.Uuid;

				bGroupListUpdateRequired = true;
			}
			else { Debug.Assert(pgRecycleBin.Uuid.EqualsValue(pdContext.RecycleBinUuid)); }
		}

		private void DeleteSelectedEntries()
		{
			PwEntry[] vSelected = GetSelectedEntries();
			if((vSelected == null) || (vSelected.Length == 0)) return;

			PwDatabase pd = m_docMgr.ActiveDatabase;
			PwGroup pgRecycleBin = pd.RootGroup.FindGroup(pd.RecycleBinUuid, true);
			bool bShiftPressed = ((Control.ModifierKeys & Keys.Shift) != Keys.None);

			bool bAtLeastOnePermanent = false;
			if(pd.RecycleBinEnabled == false) bAtLeastOnePermanent = true;
			else if(bShiftPressed) bAtLeastOnePermanent = true;
			else if(pgRecycleBin == null) { } // Not permanent
			else
			{
				foreach(PwEntry peEnum in vSelected)
				{
					if((peEnum.ParentGroup == pgRecycleBin) ||
						peEnum.ParentGroup.IsContainedIn(pgRecycleBin))
					{
						bAtLeastOnePermanent = true;
						break;
					}
				}
			}

			if(bAtLeastOnePermanent)
			{
				bool bSingle = (vSelected.Length == 1);
				if(!MessageService.AskYesNo(bSingle ? KPRes.DeleteEntriesQuestionSingle :
					KPRes.DeleteEntriesQuestion, bSingle ? KPRes.DeleteEntriesTitleSingle :
					KPRes.DeleteEntriesTitle))
					return;
			}

			bool bUpdateGroupList = false;
			DateTime dtNow = DateTime.Now;
			foreach(PwEntry pe in vSelected)
			{
				PwGroup pgParent = pe.ParentGroup;
				if(pgParent == null) continue; // Can't remove

				pgParent.Entries.Remove(pe);

				bool bPermanent = false;
				if(pd.RecycleBinEnabled == false) bPermanent = true;
				else if(bShiftPressed) bPermanent = true;
				else if(pgRecycleBin == null) { } // Recycle
				else if(pgParent == pgRecycleBin) bPermanent = true;
				else if(pgParent.IsContainedIn(pgRecycleBin)) bPermanent = true;

				if(bPermanent)
				{
					PwDeletedObject pdo = new PwDeletedObject();
					pdo.Uuid = pe.Uuid;
					pdo.DeletionTime = dtNow;
					pd.DeletedObjects.Add(pdo);
				}
				else // Recycle
				{
					EnsureRecycleBin(ref pgRecycleBin, pd, ref bUpdateGroupList);

					pgRecycleBin.AddEntry(pe, true);
					pe.Touch(false);
				}
			}

			RemoveEntriesFromList(vSelected, true);
			UpdateUI(false, null, bUpdateGroupList, null, false, null, true);
		}

		private void DeleteSelectedGroup()
		{
			PwGroup pg = GetSelectedGroup();
			if(pg == null) { Debug.Assert(false); return; }

			PwGroup pgParent = pg.ParentGroup;
			if(pgParent == null) return; // Can't remove virtual or root group

			PwDatabase pd = m_docMgr.ActiveDatabase;
			PwGroup pgRecycleBin = pd.RootGroup.FindGroup(pd.RecycleBinUuid, true);
			bool bShiftPressed = ((Control.ModifierKeys & Keys.Shift) != Keys.None);

			bool bPermanent = false;
			if(pd.RecycleBinEnabled == false) bPermanent = true;
			else if(bShiftPressed) bPermanent = true;
			else if(pgRecycleBin == null) { }
			else if(pg == pgRecycleBin) bPermanent = true;
			else if(pg.IsContainedIn(pgRecycleBin)) bPermanent = true;
			else if(pgRecycleBin.IsContainedIn(pg)) bPermanent = true;

			if(bPermanent)
			{
				string strText = KPRes.DeleteGroupInfo + MessageService.NewParagraph +
					KPRes.DeleteGroupQuestion;
				if(!MessageService.AskYesNo(strText, KPRes.DeleteGroupTitle))
					return;
			}

			pgParent.Groups.Remove(pg);

			if(bPermanent)
			{
				PwDeletedObject pdo = new PwDeletedObject();
				pdo.Uuid = pg.Uuid;
				pdo.DeletionTime = DateTime.Now;
				pd.DeletedObjects.Add(pdo);
			}
			else // Recycle
			{
				bool bDummy = false;
				EnsureRecycleBin(ref pgRecycleBin, pd, ref bDummy);

				pgRecycleBin.AddGroup(pg, true);
				pg.Touch(false);
			}

			UpdateUI(false, null, true, null, true, null, true);
		}

		private static bool GroupOnlyContainsTans(PwGroup pg, bool bAllowSubgroups)
		{
			if(!bAllowSubgroups && (pg.Groups.UCount > 0))
				return false;

			foreach(PwEntry pe in pg.Entries)
			{
				if(!PwDefs.IsTanEntry(pe)) return false;
			}

			return true;
		}

		private static string GetGroupSuffixText(PwGroup pg)
		{
			if(pg == null) { Debug.Assert(false); return string.Empty; }
			if(pg.Entries.UCount == 0) return string.Empty;
			if(GroupOnlyContainsTans(pg, true) == false) return string.Empty;

			DateTime dtNow = DateTime.Now;
			uint uValid = 0;
			foreach(PwEntry pe in pg.Entries)
			{
				if(pe.Expires && (pe.ExpiryTime <= dtNow)) { }
				else ++uValid;
			}

			return (" (" + uValid.ToString() + "/" + pg.Entries.UCount.ToString() + ")");
		}

		public void AddCustomToolBarButton(string strID, string strName, string strDesc)
		{
			if(string.IsNullOrEmpty(strID)) { Debug.Assert(false); return; } // No throw
			if(string.IsNullOrEmpty(strName)) { Debug.Assert(false); return; } // No throw

			if(m_vCustomToolBarButtons.Count == 0)
				m_toolMain.Items.Add(new ToolStripSeparator());

			ToolStripButton btn = new ToolStripButton(strName);
			btn.Tag = strID;
			btn.Click += OnCustomToolBarButtonClicked;
			if(!string.IsNullOrEmpty(strDesc)) btn.ToolTipText = strDesc;

			m_toolMain.Items.Add(btn);
			m_vCustomToolBarButtons.Add(btn);
		}

		private void OnCustomToolBarButtonClicked(object sender, EventArgs e)
		{
			ToolStripButton btn = (sender as ToolStripButton);
			if(btn == null) { Debug.Assert(false); return; }

			string strID = (btn.Tag as string);
			if(string.IsNullOrEmpty(strID)) { Debug.Assert(false); return; }

			Program.TriggerSystem.RaiseEvent(EcasEventIDs.CustomTbButtonClicked, strID);
		}
	}
}
