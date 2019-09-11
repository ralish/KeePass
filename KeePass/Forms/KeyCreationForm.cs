/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2010 Dominik Reichl <dominik.reichl@t-online.de>

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
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

using KeePass.App;
using KeePass.App.Configuration;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Keys;
using KeePassLib.Native;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class KeyCreationForm : Form
	{
		private CompositeKey m_pKey = null;
		private bool m_bCreatingNew = false;
		private IOConnectionInfo m_ioInfo = new IOConnectionInfo();

		private SecureEdit m_secPassword = new SecureEdit();
		private SecureEdit m_secRepeat = new SecureEdit();

		public CompositeKey CompositeKey
		{
			get
			{
				Debug.Assert(m_pKey != null);
				return m_pKey;
			}
		}

		public KeyCreationForm()
		{
			InitializeComponent();
			Program.Translation.ApplyTo(this);
		}

		public void InitEx(IOConnectionInfo ioInfo, bool bCreatingNew)
		{
			if(ioInfo != null) m_ioInfo = ioInfo;

			m_bCreatingNew = bCreatingNew;
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			GlobalWindowManager.AddWindow(this);

			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, BannerStyle.Default,
				Properties.Resources.B48x48_KGPG_Sign, KPRes.CreateMasterKey,
				m_ioInfo.GetDisplayName());
			this.Icon = Properties.Resources.KeePass;
			this.Text = KPRes.CreateMasterKey;

			FontUtil.AssignDefaultBold(m_cbPassword);
			FontUtil.AssignDefaultBold(m_cbKeyFile);
			FontUtil.AssignDefaultBold(m_cbUserAccount);

			m_ttRect.SetToolTip(m_cbHidePassword, KPRes.TogglePasswordAsterisks);
			m_ttRect.SetToolTip(m_btnSaveKeyFile, KPRes.KeyFileCreate);
			m_ttRect.SetToolTip(m_btnOpenKeyFile, KPRes.KeyFileUseExisting);

			if(!m_bCreatingNew)
				m_lblIntro.Text = KPRes.ChangeMasterKeyIntroShort;

			m_secPassword.Attach(m_tbPassword, ProcessTextChangedPassword, true);
			m_secRepeat.Attach(m_tbRepeatPassword, null, true);
			m_cbHidePassword.Checked = true;

			m_cbPassword.Checked = true;
			ProcessTextChangedPassword(sender, e); // Update quality estimation

			m_cmbKeyFile.Items.Add(KPRes.NoKeyFileSpecifiedMeta);
			foreach(KeyProvider prov in Program.KeyProviderPool)
				m_cmbKeyFile.Items.Add(prov.Name);

			m_cmbKeyFile.SelectedIndex = 0;

			UIUtil.ApplyKeyUIFlags(Program.Config.UI.KeyCreationFlags,
				m_cbPassword, m_cbKeyFile, m_cbUserAccount, m_cbHidePassword);

			if(WinUtil.IsWindows9x || NativeLib.IsUnix())
			{
				UIUtil.SetChecked(m_cbUserAccount, false);
				UIUtil.SetEnabled(m_cbUserAccount, false);
				UIUtil.SetEnabled(m_lblWindowsAccDesc, false);
				UIUtil.SetEnabled(m_lblWindowsAccDesc2, false);
			}

			CustomizeForScreenReader();
			EnableUserControls();
		}

		private void CustomizeForScreenReader()
		{
			if(!Program.Config.UI.OptimizeForScreenReader) return;

			m_cbHidePassword.Text = KPRes.HideUsingAsterisks;
		}

		private void CleanUpEx()
		{
			m_secPassword.Detach();
			m_secRepeat.Detach();
		}

		private bool CreateCompositeKey()
		{
			m_pKey = new CompositeKey();

			if(m_cbPassword.Checked) // Use a password
			{
				if(m_secPassword.ContentsEqualTo(m_secRepeat) == false)
				{
					MessageService.ShowWarning(KPRes.PasswordRepeatFailed);
					return false;
				}

				if(m_secPassword.TextLength == 0)
				{
					if(!MessageService.AskYesNo(KPRes.EmptyMasterPw +
						MessageService.NewParagraph + KPRes.EmptyMasterPwHint +
						MessageService.NewParagraph + KPRes.EmptyMasterPwQuestion,
						null, false))
					{
						return false;
					}
				}

				uint uMinLen = Program.Config.Security.MasterPassword.MinimumLength;
				if(m_secPassword.TextLength < uMinLen)
				{
					string strML = KPRes.MasterPasswordMinLengthFailed;
					strML = strML.Replace(@"{PARAM}", uMinLen.ToString());
					MessageService.ShowWarning(strML);
					return false;
				}

				byte[] pb = m_secPassword.ToUtf8();

				uint uMinQual = Program.Config.Security.MasterPassword.MinimumQuality;
				if(QualityEstimation.EstimatePasswordBits(pb) < uMinQual)
				{
					string strMQ = KPRes.MasterPasswordMinQualityFailed;
					strMQ = strMQ.Replace(@"{PARAM}", uMinQual.ToString());
					MessageService.ShowWarning(strMQ);
					Array.Clear(pb, 0, pb.Length);
					return false;
				}

				string strValRes = Program.KeyValidatorPool.Validate(pb,
					KeyValidationType.MasterPassword);
				if(strValRes != null)
				{
					MessageService.ShowWarning(strValRes);
					Array.Clear(pb, 0, pb.Length);
					return false;
				}

				m_pKey.AddUserKey(new KcpPassword(pb));
				Array.Clear(pb, 0, pb.Length);
			}

			string strKeyFile = m_cmbKeyFile.Text;
			bool bIsKeyProv = Program.KeyProviderPool.IsKeyProvider(strKeyFile);

			if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				(bIsKeyProv == false))
			{
				try { m_pKey.AddUserKey(new KcpKeyFile(strKeyFile)); }
				catch(Exception exKF)
				{
					MessageService.ShowWarning(strKeyFile, KPRes.KeyFileError, exKF);
					return false;
				}
			}
			else if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				(bIsKeyProv == true))
			{
				KeyProviderQueryContext ctxKP = new KeyProviderQueryContext(m_ioInfo, true);

				bool bPerformHash;
				byte[] pbCustomKey = Program.KeyProviderPool.GetKey(strKeyFile, ctxKP,
					out bPerformHash);
				if((pbCustomKey != null) && (pbCustomKey.Length > 0))
				{
					try { m_pKey.AddUserKey(new KcpCustomKey(strKeyFile, pbCustomKey, bPerformHash)); }
					catch(Exception exCKP)
					{
						MessageService.ShowWarning(exCKP);
						return false;
					}

					Array.Clear(pbCustomKey, 0, pbCustomKey.Length);
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

		private void EnableUserControls()
		{
			m_tbPassword.Enabled = m_tbRepeatPassword.Enabled =
				m_lblRepeatPassword.Enabled = m_lblQualityBits.Enabled =
				m_lblEstimatedQuality.Enabled = m_cbPassword.Checked;
			if((Program.Config.UI.KeyCreationFlags &
				(ulong)AceKeyUIFlags.DisableHidePassword) == 0)
				m_cbHidePassword.Enabled = m_cbPassword.Checked;

			m_btnOpenKeyFile.Enabled = m_btnSaveKeyFile.Enabled =
				m_cmbKeyFile.Enabled = m_cbKeyFile.Checked;

			string strKeyFile = m_cmbKeyFile.Text;

			if(!m_cbPassword.Checked && !m_cbKeyFile.Checked && !m_cbUserAccount.Checked)
				m_btnCreate.Enabled = false;
			else if(m_cbKeyFile.Checked && strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta))
				m_btnCreate.Enabled = false;
			else m_btnCreate.Enabled = true;

			SetHidePassword(m_cbHidePassword.Checked, false);

			m_ttRect.SetToolTip(m_cmbKeyFile, strKeyFile);
		}

		private void SetHidePassword(bool bHide, bool bUpdateCheckBox)
		{
			if(bUpdateCheckBox) m_cbHidePassword.Checked = bHide;

			m_secPassword.EnableProtection(bHide);
			m_secRepeat.EnableProtection(bHide);
		}

		private void OnCheckedPassword(object sender, EventArgs e)
		{
			EnableUserControls();

			if(m_cbPassword.Checked) m_tbPassword.Focus();
		}

		private void OnCheckedKeyFile(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnCheckedHidePassword(object sender, EventArgs e)
		{
			bool bHide = m_cbHidePassword.Checked;
			if(!bHide && !AppPolicy.Try(AppPolicyId.UnhidePasswords))
			{
				m_cbHidePassword.Checked = true;
				return;
			}

			SetHidePassword(bHide, false);
			m_tbPassword.Focus();
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			if(!CreateCompositeKey()) this.DialogResult = DialogResult.None;
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
			m_pKey = null;
		}

		private void ProcessTextChangedPassword(object sender, EventArgs e)
		{
			byte[] pbUTF8 = m_secPassword.ToUtf8();
			uint uBits = QualityEstimation.EstimatePasswordBits(pbUTF8);
			MemUtil.ZeroByteArray(pbUTF8);

			m_lblQualityBits.Text = uBits.ToString() + " " + KPRes.Bits;
			int iPos = (int)((100 * uBits) / (256 / 2));
			if(iPos < 0) iPos = 0; else if(iPos > 100) iPos = 100;
			m_pbPasswordQuality.Value = iPos;
		}

		private void OnClickKeyFileCreate(object sender, EventArgs e)
		{
			SaveFileDialog sfd = UIUtil.CreateSaveFileDialog(KPRes.KeyFileCreate,
				UrlUtil.StripExtension(UrlUtil.GetFileName(m_ioInfo.Path)) + "." +
				AppDefs.FileExtension.KeyFile, UIUtil.CreateFileTypeFilter("key",
				KPRes.KeyFiles, true), 1, "key", true);

			if(sfd.ShowDialog() == DialogResult.OK)
			{
				EntropyForm dlg = new EntropyForm();
				if(dlg.ShowDialog() == DialogResult.OK)
				{
					byte[] pbAdditionalEntropy = dlg.GeneratedEntropy;

					try
					{
						KcpKeyFile.Create(sfd.FileName, pbAdditionalEntropy);
						
						string str = sfd.FileName;
						m_cmbKeyFile.Items.Add(str);
						m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
					}
					catch(Exception exKC)
					{
						MessageService.ShowWarning(exKC);
					}
				}
			}

			EnableUserControls();
		}

		private void OnClickKeyFileBrowse(object sender, EventArgs e)
		{
			OpenFileDialog ofd = UIUtil.CreateOpenFileDialog(KPRes.KeyFileUseExisting,
				UIUtil.CreateFileTypeFilter("key", KPRes.KeyFiles, true), 2, null,
				false, true);

			if(ofd.ShowDialog() == DialogResult.OK)
			{
				string str = ofd.FileName;
				m_cmbKeyFile.Items.Add(str);
				m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
			}

			EnableUserControls();
		}

		private void OnWinUserCheckedChanged(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			GlobalWindowManager.RemoveWindow(this);
		}

		private void OnBtnHelp(object sender, EventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.KeySources, null);
		}

		private void OnKeyFileSelectedIndexChanged(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e)
		{
			CleanUpEx();
		}
	}
}
