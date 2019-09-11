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
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Diagnostics;

using KeePass.UI;
using KeePass.Resources;

using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Delegates;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	/// <summary>
	/// Form in which the user can configure search parameters. This
	/// dialog performs the search itself and returns the result
	/// in the <c>SearchResultsGroup</c> property.
	/// </summary>
	public partial class SearchForm : Form, IGwmWindow
	{
		private PwGroup m_pgRoot = null;
		private PwGroup m_pgResultsGroup = null;

		public bool CanCloseWithoutDataLoss { get { return true; } }

		/// <summary>
		/// After closing the dialog, this property contains the search results.
		/// </summary>
		public PwGroup SearchResultsGroup
		{
			get { return m_pgResultsGroup; }
		}

		/// <summary>
		/// Default constructor.
		/// </summary>
		public SearchForm()
		{
			InitializeComponent();
			Program.Translation.ApplyTo(this);
		}

		/// <summary>
		/// Initialize the form. Must be called before the dialog is displayed.
		/// </summary>
		/// <param name="pwRoot">Data source group. This group will be searched.</param>
		public void InitEx(PwGroup pwRoot)
		{
			m_pgRoot = pwRoot;
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			Debug.Assert(m_pgRoot != null); if(m_pgRoot == null) throw new InvalidOperationException();

			GlobalWindowManager.AddWindow(this, this);

			string strTitle = KPRes.SearchTitle;
			if((m_pgRoot != null) && (m_pgRoot.ParentGroup != null))
				strTitle += " - " + m_pgRoot.Name;

			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, BannerStyle.Default,
				Properties.Resources.B48x48_XMag, strTitle,
				KPRes.SearchDesc);
			this.Icon = Properties.Resources.KeePass;

			m_cbTitle.Checked = Program.Config.Defaults.SearchParameters.SearchInTitles;
			m_cbUserName.Checked = Program.Config.Defaults.SearchParameters.SearchInUserNames;
			m_cbURL.Checked = Program.Config.Defaults.SearchParameters.SearchInUrls;
			m_cbPassword.Checked = Program.Config.Defaults.SearchParameters.SearchInPasswords;
			m_cbNotes.Checked = Program.Config.Defaults.SearchParameters.SearchInNotes;
			m_cbOtherFields.Checked = Program.Config.Defaults.SearchParameters.SearchInOther;

			StringComparison sc = Program.Config.Defaults.SearchParameters.ComparisonMode;
			m_cbCaseSensitive.Checked = ((sc != StringComparison.CurrentCultureIgnoreCase) &&
				(sc != StringComparison.InvariantCultureIgnoreCase) &&
				(sc != StringComparison.OrdinalIgnoreCase));

			m_cbRegEx.Checked = Program.Config.Defaults.SearchParameters.RegularExpression;
			m_cbExcludeExpired.Checked = Program.Config.Defaults.SearchParameters.ExcludeExpired;

			this.ActiveControl = m_tbSearch;
			m_tbSearch.Focus();
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			SearchParameters sp = GetSearchParameters(true);

			if(sp.RegularExpression) // Validate regular expression
			{
				try { new Regex(sp.SearchString); }
				catch(Exception exReg)
				{
					MessageService.ShowWarning(exReg.Message);
					this.DialogResult = DialogResult.None;
					return;
				}
			}

			string strGroupName = KPRes.SearchGroupName + " (\"" + sp.SearchString + "\" ";
			strGroupName += KPRes.SearchResultsInSeparator + " ";
			strGroupName += m_pgRoot.Name + ")";
			PwGroup pgResults = new PwGroup(true, true, strGroupName, PwIcon.EMailSearch);
			pgResults.IsVirtual = true;

			PwObjectList<PwEntry> listResults = pgResults.Entries;

			try { m_pgRoot.SearchEntries(sp, listResults); }
			catch(Exception exFind) { MessageService.ShowWarning(exFind); }

			m_pgResultsGroup = pgResults;

			sp.SearchString = string.Empty; // Clear for saving
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
			GetSearchParameters(false);
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			GlobalWindowManager.RemoveWindow(this);
		}

		private SearchParameters GetSearchParameters(bool bWithText)
		{
			SearchParameters sp = Program.Config.Defaults.SearchParameters;

			if(bWithText) sp.SearchString = m_tbSearch.Text;
			else sp.SearchString = string.Empty;

			sp.RegularExpression = m_cbRegEx.Checked;

			sp.SearchInTitles = m_cbTitle.Checked;
			sp.SearchInUserNames = m_cbUserName.Checked;
			sp.SearchInPasswords = m_cbPassword.Checked;
			sp.SearchInUrls = m_cbURL.Checked;
			sp.SearchInNotes = m_cbNotes.Checked;
			sp.SearchInOther = m_cbOtherFields.Checked;

			sp.ComparisonMode = (m_cbCaseSensitive.Checked ?
				StringComparison.InvariantCulture :
				StringComparison.InvariantCultureIgnoreCase);

			sp.ExcludeExpired = m_cbExcludeExpired.Checked;

			return sp;
		}
	}
}
