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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.UI;
using KeePass.Resources;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class LanguageForm : Form, IGwmWindow
	{
		private List<AvTranslation> m_lTranslations = null;

		public bool CanCloseWithoutDataLoss { get { return true; } }

		public LanguageForm()
		{
			InitializeComponent();
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			GlobalWindowManager.AddWindow(this, this);

			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, BannerStyle.Default,
				Properties.Resources.B48x48_Keyboard_Layout,
				KPRes.SelectLanguage, KPRes.SelectLanguageDesc);
			this.Icon = Properties.Resources.KeePass;
			this.Text = KPRes.SelectLanguage;

			int nWidth = m_lvLanguages.ClientRectangle.Width / 4;
			m_lvLanguages.Columns.Add(KPRes.AvailableLanguages, nWidth);
			m_lvLanguages.Columns.Add(KPRes.Version, nWidth);
			m_lvLanguages.Columns.Add(KPRes.Author, nWidth);
			m_lvLanguages.Columns.Add(KPRes.Contact, nWidth);

			ListViewItem lvi = m_lvLanguages.Items.Add("English", 0);
			lvi.SubItems.Add(PwDefs.VersionString);
			lvi.SubItems.Add(AppDefs.DefaultTrlAuthor);
			lvi.SubItems.Add(AppDefs.DefaultTrlContact);

			string strExe = WinUtil.GetExecutable();
			string strPath = UrlUtil.GetFileDirectory(strExe, false);
			m_lTranslations = TranslationUtil.GetAvailableTranslations(strPath);

			foreach(AvTranslation trl in m_lTranslations)
			{
				lvi = m_lvLanguages.Items.Add(trl.LanguageEnglishName, 0);
				lvi.SubItems.Add(trl.VersionForApp);
				lvi.SubItems.Add(trl.AuthorName);
				lvi.SubItems.Add(trl.AuthorContact);
			}
		}

		private void OnBtnClose(object sender, EventArgs e)
		{
		}

		private void OnLanguagesItemActivate(object sender, EventArgs e)
		{
			ListView.SelectedListViewItemCollection lvic = m_lvLanguages.SelectedItems;
			if((lvic == null) || (lvic.Count != 1)) return;

			if(lvic[0].Index == 0) // First item selected = English
			{
				if((Program.Config.Application.Language.Length == 0) ||
					(Program.Config.Application.Language == AppDefs.DefaultLanguage))
					return; // Is built-English already

				Program.Config.Application.Language = AppDefs.DefaultLanguage;
			}
			else
			{
				string strSelID = m_lTranslations[lvic[0].Index - 1].LanguageID;

				if(strSelID == Program.Config.Application.Language) return;

				Program.Config.Application.Language = strSelID;
			}

			this.DialogResult = DialogResult.OK;
			this.Close();
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			GlobalWindowManager.RemoveWindow(this);
		}

		private void OnBtnGetMore(object sender, EventArgs e)
		{
			WinUtil.OpenUrl(PwDefs.TranslationsUrl, null);
		}
	}
}
