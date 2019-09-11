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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

using KeePass.App;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Native;
using KeePassLib.Utility;
using KeePassLib.Serialization;

namespace KeePass.Forms
{
	public partial class KeyPromptForm : Form
	{
		private CompositeKey m_pKey = null;
		private IOConnectionInfo m_ioInfo = new IOConnectionInfo();

		private bool m_bRedirectActivation = false;
		private bool m_bCanExit = false;
		private bool m_bHasExited = false;

		private SecureEdit m_secPassword = new SecureEdit();

		private bool m_bInitializing = false;

		private volatile List<string> m_vSuggestions = new List<string>();
		private volatile bool m_bSuggestionsReady = false;

		public CompositeKey CompositeKey
		{
			get
			{
				Debug.Assert(m_pKey != null);
				return m_pKey;
			}
		}

		public bool HasClosedWithExit
		{
			get { return m_bHasExited; }
		}

		public KeyPromptForm()
		{
			InitializeComponent();
			Program.Translation.ApplyTo(this);
		}

		public void InitEx(IOConnectionInfo ioInfo, bool bCanExit,
			bool bRedirectActivation)
		{
			if(ioInfo != null) m_ioInfo = ioInfo;

			m_bCanExit = bCanExit;
			m_bRedirectActivation = bRedirectActivation;
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			GlobalWindowManager.AddWindow(this);
			if(m_bRedirectActivation) Program.MainForm.RedirectActivationPush(this);

			m_bInitializing = true;

			string strBannerDesc = WinUtil.CompactPath(m_ioInfo.Path, 45);
			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, BannerStyle.Default,
				Properties.Resources.B48x48_KGPG_Key2, KPRes.EnterCompositeKey,
				strBannerDesc);
			this.Icon = Properties.Resources.KeePass;

			m_ttRect.SetToolTip(m_cbHidePassword, KPRes.TogglePasswordAsterisks);
			m_ttRect.SetToolTip(m_btnOpenKeyFile, KPRes.KeyFileSelect);

			string strNameEx = UrlUtil.GetFileName(m_ioInfo.Path);
			if(strNameEx.Length > 0) this.Text += " - " + strNameEx;

			m_tbPassword.Text = string.Empty;
			m_secPassword.Attach(m_tbPassword, ProcessTextChangedPassword, true);

			m_cmbKeyFile.Items.Add(KPRes.NoKeyFileSpecifiedMeta);
			m_cmbKeyFile.SelectedIndex = 0;

			if((Program.CommandLineArgs.FileName != null) &&
				(m_ioInfo.Path == Program.CommandLineArgs.FileName))
			{
				string str;

				str = Program.CommandLineArgs[AppDefs.CommandLineOptions.Password];
				if(str != null)
				{
					m_cbPassword.Checked = true;
					m_tbPassword.Text = str;
				}

				str = Program.CommandLineArgs[AppDefs.CommandLineOptions.KeyFile];
				if(str != null)
				{
					m_cbKeyFile.Checked = true;

					m_cmbKeyFile.Items.Add(str);
					m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
				}

				str = Program.CommandLineArgs[AppDefs.CommandLineOptions.PreSelect];
				if(str != null)
				{
					m_cbKeyFile.Checked = true;

					m_cmbKeyFile.Items.Add(str);
					m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
				}
			}

			m_cbHidePassword.Checked = true;
			OnCheckedHidePassword(sender, e);

			Debug.Assert(m_cmbKeyFile.Text.Length != 0);

			m_btnExit.Enabled = m_bCanExit;
			m_btnExit.Visible = m_bCanExit;

			if(WinUtil.IsWindows9x || NativeLib.IsUnix())
				m_cbUserAccount.Enabled = false;

			CustomizeForScreenReader();
			EnableUserControls();

			m_bInitializing = false;

			// Local, but thread will continue to run anyway
			Thread th = new Thread(new ThreadStart(this.AsyncFormLoad));
			th.Start();

			this.BringToFront();
			this.Activate();
			m_tbPassword.Focus();
		}

		private void CustomizeForScreenReader()
		{
			if(!Program.Config.UI.OptimizeForScreenReader) return;

			m_cbHidePassword.Text = KPRes.HideUsingAsterisks;
			m_btnOpenKeyFile.Text = KPRes.SelectFile;
		}

		private void CleanUpEx()
		{
			m_secPassword.Detach();
		}

		private bool CreateCompositeKey()
		{
			m_pKey = new CompositeKey();

			if(m_cbPassword.Checked) // Use a password
			{
				byte[] pb = m_secPassword.ToUtf8();
				m_pKey.AddUserKey(new KcpPassword(pb));
				Array.Clear(pb, 0, pb.Length);
			}

			string strKeyFile = m_cmbKeyFile.Text;
			Debug.Assert(strKeyFile != null); if(strKeyFile == null) strKeyFile = string.Empty;
			bool bIsProvKey = Program.KeyProviderPool.IsKeyProvider(strKeyFile);

			if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				(bIsProvKey == false))
			{
				if(ValidateKeyFileLocation() == false) return false;

				try { m_pKey.AddUserKey(new KcpKeyFile(strKeyFile)); }
				catch(Exception)
				{
					MessageService.ShowWarning(strKeyFile, KPRes.KeyFileError);
					return false;
				}
			}
			else if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				(bIsProvKey == true))
			{
				KeyProviderQueryContext ctxKP = new KeyProviderQueryContext(m_ioInfo, false);

				bool bPerformHash;
				byte[] pbProvKey = Program.KeyProviderPool.GetKey(strKeyFile, ctxKP,
					out bPerformHash);
				if((pbProvKey != null) && (pbProvKey.Length > 0))
				{
					try { m_pKey.AddUserKey(new KcpCustomKey(strKeyFile, pbProvKey, bPerformHash)); }
					catch(Exception exCKP)
					{
						MessageService.ShowWarning(exCKP);
						return false;
					}

					Array.Clear(pbProvKey, 0, pbProvKey.Length);
				}
				else return false; // Provider has shown error message
			}

			if(m_cbUserAccount.Checked)
			{
				try { m_pKey.AddUserKey(new KcpUserAccount()); }
				catch(Exception exUA)
				{
					MessageService.ShowWarning(exUA);
					return false;
				}
			}

			return true;
		}

		private bool ValidateKeyFileLocation()
		{
			string strKeyFile = m_cmbKeyFile.Text;
			Debug.Assert(strKeyFile != null); if(strKeyFile == null) strKeyFile = string.Empty;
			if(strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) return true;

			if(Program.KeyProviderPool.IsKeyProvider(strKeyFile)) return true;

			bool bSuccess = true;

			if(File.Exists(strKeyFile) == false)
			{
				MessageService.ShowWarning(strKeyFile, KPRes.FileNotFoundError);
				bSuccess = false;
			}

			if(bSuccess == false)
			{
				int nPos = m_cmbKeyFile.Items.IndexOf(strKeyFile);
				if(nPos >= 0) m_cmbKeyFile.Items.RemoveAt(nPos);

				m_cmbKeyFile.SelectedIndex = 0;
			}

			return bSuccess;
		}

		private void EnableUserControls()
		{
			string strKeyFile = m_cmbKeyFile.Text;
			Debug.Assert(strKeyFile != null); if(strKeyFile == null) strKeyFile = string.Empty;
			if(m_cbKeyFile.Checked && strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta))
				m_btnOK.Enabled = false;
			else m_btnOK.Enabled = true;

			bool bExclusiveProv = false;
			KeyProvider prov = Program.KeyProviderPool.Get(strKeyFile);
			if(prov != null) bExclusiveProv = prov.Exclusive;

			if(bExclusiveProv)
			{
				m_tbPassword.Text = string.Empty;
				UIUtil.SetChecked(m_cbPassword, false);
				UIUtil.SetChecked(m_cbUserAccount, false);
			}
			UIUtil.SetEnabled(m_cbPassword, !bExclusiveProv);
			UIUtil.SetEnabled(m_tbPassword, !bExclusiveProv);
			UIUtil.SetEnabled(m_cbHidePassword, !bExclusiveProv);
			UIUtil.SetEnabled(m_cbUserAccount, !bExclusiveProv);
		}

		private void OnCheckedPassword(object sender, EventArgs e)
		{
			if(m_cbPassword.Checked) m_tbPassword.Focus();
		}

		private void OnCheckedKeyFile(object sender, EventArgs e)
		{
			if(m_bInitializing) return;

			if(!m_cbKeyFile.Checked)
				m_cmbKeyFile.SelectedIndex = 0;

			EnableUserControls();
		}

		private void ProcessTextChangedPassword(object sender, EventArgs e)
		{
			m_cbPassword.Checked = (m_tbPassword.Text.Length != 0);
		}

		private void OnCheckedHidePassword(object sender, EventArgs e)
		{
			m_secPassword.EnableProtection(m_cbHidePassword.Checked);
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			if(!CreateCompositeKey()) this.DialogResult = DialogResult.None;
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
			m_pKey = null;
		}

		private void OnBtnHelp(object sender, EventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.KeySources, null);
		}

		private void OnClickKeyFileBrowse(object sender, EventArgs e)
		{
			string strFilter = UIUtil.CreateFileTypeFilter("key", KPRes.KeyFiles, true);
			OpenFileDialog ofd = UIUtil.CreateOpenFileDialog(KPRes.KeyFileSelect,
				strFilter, 2, null, false, true);

			if(ofd.ShowDialog() == DialogResult.OK)
			{
				m_cbKeyFile.Checked = true;

				m_cmbKeyFile.Items.Add(ofd.FileName);
				m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
			}

			EnableUserControls();
		}

		private void OnKeyFileSelectedIndexChanged(object sender, EventArgs e)
		{
			if(m_bInitializing) return;

			string strKeyFile = m_cmbKeyFile.Text;
			Debug.Assert(strKeyFile != null); if(strKeyFile == null) strKeyFile = string.Empty;
			if(strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta) == false)
			{
				if(ValidateKeyFileLocation())
					m_cbKeyFile.Checked = true;
			}
			else m_cbKeyFile.Checked = false;

			EnableUserControls();
		}

		private void AsyncFormLoad()
		{
			try { PopulateKeyFileSuggestions(); }
			catch(Exception) { Debug.Assert(false); }
		}

		private void PopulateKeyFileSuggestions()
		{
			bool bSearchOnRemovable = Program.Config.Integration.SearchKeyFilesOnRemovableMedia;

			foreach(DriveInfo di in DriveInfo.GetDrives())
			{
				if(di.DriveType == DriveType.NoRootDirectory)
					continue;
				else if((di.DriveType == DriveType.Removable) && !bSearchOnRemovable)
					continue;
				else if(di.DriveType == DriveType.CDRom)
					continue;

				if(di.IsReady == false) continue;

				try
				{
					FileInfo[] vFiles = di.RootDirectory.GetFiles(@"*." +
						AppDefs.FileExtension.KeyFile, SearchOption.TopDirectoryOnly);
					if(vFiles == null) continue;

					foreach(FileInfo fi in vFiles)
						m_vSuggestions.Add(fi.FullName);
				}
				catch(Exception) { Debug.Assert(false); }
			}

			foreach(KeyProvider prov in Program.KeyProviderPool)
				m_vSuggestions.Add(prov.Name);

			m_bSuggestionsReady = true;
		}

		private void OnKeyFileFillerTimerTick(object sender, EventArgs e)
		{
			if(m_bSuggestionsReady)
			{
				m_bSuggestionsReady = false;
				m_timerKeyFileFiller.Enabled = false;

				foreach(string str in m_vSuggestions)
					m_cmbKeyFile.Items.Add(str);

				m_vSuggestions.Clear();

				if(m_cmbKeyFile.SelectedIndex == 0)
				{
					string strRemKeyFile = Program.Config.Defaults.GetKeySource(
						m_ioInfo, true);
					if(!string.IsNullOrEmpty(strRemKeyFile))
					{
						m_cmbKeyFile.Items.Add(strRemKeyFile);
						m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
					}

					string strRemKeyProv = Program.Config.Defaults.GetKeySource(
						m_ioInfo, false);
					if(!string.IsNullOrEmpty(strRemKeyProv))
					{
						int iProv = m_cmbKeyFile.FindStringExact(strRemKeyProv);
						if(iProv >= 0) m_cmbKeyFile.SelectedIndex = iProv;
					}
				}
			}
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			GlobalWindowManager.RemoveWindow(this);
		}

		private void OnBtnExit(object sender, EventArgs e)
		{
			if(m_bCanExit == false)
			{
				Debug.Assert(false);
				this.DialogResult = DialogResult.None;
				return;
			}

			m_bHasExited = true;
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e)
		{
			if(m_bRedirectActivation) Program.MainForm.RedirectActivationPop();
			CleanUpEx();
		}
	}
}
