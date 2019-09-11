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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

using KeePass.Forms;
using KeePass.Resources;

using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;
using KeePassLib.Serialization;

namespace KeePass.Util
{
	/// <summary>
	/// This class contains various static functions for entry operations.
	/// </summary>
	public static class EntryUtil
	{
		/// <summary>
		/// Save all attachments of an array of entries to a directory.
		/// </summary>
		/// <param name="vEntries">Array of entries whose attachments are extracted and saved.</param>
		/// <param name="strBasePath">Directory in which the attachments are stored.</param>
		public static void SaveEntryAttachments(PwEntry[] vEntries, string strBasePath)
		{
			Debug.Assert(vEntries != null); if(vEntries == null) return;
			Debug.Assert(strBasePath != null); if(strBasePath == null) return;

			string strPath = UrlUtil.EnsureTerminatingSeparator(strBasePath, false);
			bool bCancel = false;

			foreach(PwEntry pe in vEntries)
			{
				foreach(KeyValuePair<string, ProtectedBinary> kvp in pe.Binaries)
				{
					string strFile = strPath + kvp.Key;

					if(File.Exists(strFile))
					{
						string strMsg = KPRes.FileExistsAlready + MessageService.NewLine;
						strMsg += strFile + MessageService.NewParagraph;
						strMsg += KPRes.OverwriteExistingFileQuestion;

						DialogResult dr = MessageService.Ask(strMsg, null,
							MessageBoxButtons.YesNoCancel);

						if(dr == DialogResult.Cancel)
						{
							bCancel = true;
							break;
						}
						else if(dr == DialogResult.Yes)
						{
							try { File.Delete(strFile); }
							catch(Exception exDel)
							{
								MessageService.ShowWarning(strFile, exDel);
								continue;
							}
						}
						else continue; // DialogResult.No
					}

					try { File.WriteAllBytes(strFile, kvp.Value.ReadData()); }
					catch(Exception exWrite)
					{
						MessageService.ShowWarning(strFile, exWrite);
					}
				}
				if(bCancel) break;
			}
		}

		public const string ClipFormatEntries = "KeePassEntries";
		private static byte[] AdditionalEntropy = { 0xF8, 0x03, 0xFA, 0x51, 0x87, 0x18, 0x49, 0x5C };

		public static void CopyEntriesToClipboard(PwDatabase pwDatabase, PwEntry[] vEntries)
		{
			MemoryStream ms = new MemoryStream();
			GZipStream gz = new GZipStream(ms, CompressionMode.Compress);
			Kdb4File.WriteEntries(gz, pwDatabase, vEntries);

			byte[] pbFinal = ProtectedData.Protect(ms.ToArray(), AdditionalEntropy, DataProtectionScope.CurrentUser);

			ClipboardUtil.Copy(pbFinal, ClipFormatEntries, true);

			gz.Close(); ms.Close();
		}

		public static void PasteEntriesFromClipboard(PwDatabase pwDatabase, PwGroup pgStorage)
		{
			if(Clipboard.ContainsData(ClipFormatEntries) == false) return;

			byte[] pbEnc = (byte[])Clipboard.GetData(ClipFormatEntries);

			byte[] pbPlain = ProtectedData.Unprotect(pbEnc, AdditionalEntropy, DataProtectionScope.CurrentUser);
			MemoryStream ms = new MemoryStream(pbPlain, false);
			GZipStream gz = new GZipStream(ms, CompressionMode.Decompress);

			List<PwEntry> vEntries = Kdb4File.ReadEntries(pwDatabase, gz);

			foreach(PwEntry pe in vEntries)
			{
				ProtectedString ps = pe.Strings.Get(PwDefs.TitleField);
				if(ps != null) ps.EnableProtection(pwDatabase.MemoryProtection.ProtectTitle);

				ps = pe.Strings.Get(PwDefs.UserNameField);
				if(ps != null) ps.EnableProtection(pwDatabase.MemoryProtection.ProtectUserName);

				ps = pe.Strings.Get(PwDefs.PasswordField);
				if(ps != null) ps.EnableProtection(pwDatabase.MemoryProtection.ProtectPassword);

				ps = pe.Strings.Get(PwDefs.UrlField);
				if(ps != null) ps.EnableProtection(pwDatabase.MemoryProtection.ProtectUrl);

				ps = pe.Strings.Get(PwDefs.NotesField);
				if(ps != null) ps.EnableProtection(pwDatabase.MemoryProtection.ProtectNotes);

				pgStorage.Entries.Add(pe);
			}

			gz.Close(); ms.Close();
		}

		public static string FillPlaceholders(string strText, PwEntry pe,
			bool bDataAsKeySequence)
		{
			string str = strText;

			if(str.ToUpper().IndexOf(@"{PICKPASSWORDCHARS}") >= 0)
			{
				ProtectedString ps = pe.Strings.Get(PwDefs.PasswordField);
				byte[] pb = ps.ReadUtf8();
				bool bNotEmpty = (pb.Length > 0);
				Array.Clear(pb, 0, pb.Length);

				if(bNotEmpty)
				{
					CharPickerForm cpf = new CharPickerForm();
					cpf.InitEx(ps, true, true);

					if(cpf.ShowDialog() == DialogResult.OK)
					{
						str = StrUtil.ReplaceCaseInsensitive(str, @"{PICKPASSWORDCHARS}",
							cpf.SelectedCharacters, false, bDataAsKeySequence);
					}
				}

				str = StrUtil.ReplaceCaseInsensitive(str, @"{PICKPASSWORDCHARS}",
					new ProtectedString(false), false, bDataAsKeySequence);
			}

			return str;
		}
	}
}
