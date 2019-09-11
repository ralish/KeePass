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
using System.IO;
using System.Xml;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Drawing;
using System.Globalization;
using System.Drawing.Imaging;

#if !KeePassLibSD
using System.IO.Compression;
#else
using KeePassLibSD;
#endif

using KeePassLib.Collections;
using KeePassLib.Cryptography;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Delegates;
using KeePassLib.Interfaces;
using KeePassLib.Resources;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePassLib.Serialization
{
	/// <summary>
	/// Serialization to KeePass KDB files.
	/// </summary>
	public sealed partial class Kdb4File
	{
		public void Save(string strFile, Kdb4Format format, IStatusLogger slLogger)
		{
			IOConnectionInfo ioc = IOConnectionInfo.FromPath(strFile);
			this.Save(IOConnection.OpenWrite(ioc), format, slLogger);
		}

		/// <summary>
		/// Save the contents of the current <c>PwDatabase</c> to a KDB file.
		/// </summary>
		/// <param name="sSaveTo">Stream to write the KDB file into.</param>
		/// <param name="format">Format of the file to create.</param>
		/// <param name="slLogger">Logger that recieves status information.</param>
		public void Save(Stream sSaveTo, Kdb4Format format, IStatusLogger slLogger)
		{
			Debug.Assert(sSaveTo != null);
			if(sSaveTo == null) throw new ArgumentNullException("sSaveTo");

			m_format = format;
			m_slLogger = slLogger;

			UTF8Encoding encNoBom = new UTF8Encoding(false, false);

			try
			{
				m_pbMasterSeed = CryptoRandom.GetRandomBytes(32);
				m_pbTransformSeed = CryptoRandom.GetRandomBytes(32);
				m_pbEncryptionIV = CryptoRandom.GetRandomBytes(16);

				m_pbProtectedStreamKey = CryptoRandom.GetRandomBytes(32);
				m_randomStream = new CryptoRandomStream(CrsAlgorithm.ArcFour, m_pbProtectedStreamKey);

				m_pbStreamStartBytes = CryptoRandom.GetRandomBytes(32);

				Stream writerStream;
				BinaryWriter bw = null;
				if(m_format == Kdb4Format.Default)
				{
					bw = new BinaryWriter(sSaveTo, encNoBom);
					WriteHeader(bw);

					Stream sEncrypted = AttachStreamEncryptor(sSaveTo);
					if((sEncrypted == null) || (sEncrypted == sSaveTo))
						throw new SecurityException(KLRes.CryptoStreamFailed);

					sEncrypted.Write(m_pbStreamStartBytes, 0, m_pbStreamStartBytes.Length);

					Stream sHashed = new HashedBlockStream(sEncrypted, true);

					if(m_pwDatabase.Compression == PwCompressionAlgorithm.GZip)
						writerStream = new GZipStream(sHashed, CompressionMode.Compress);
					else
						writerStream = sHashed;
				}
				else if(m_format == Kdb4Format.PlainXml)
					writerStream = sSaveTo;
				else { Debug.Assert(false); throw new FormatException("KdbFormat"); }

				m_xmlWriter = new XmlTextWriter(writerStream, Encoding.UTF8);
				WriteDocument();

				m_xmlWriter.Flush();
				m_xmlWriter.Close();
				writerStream.Close();

				GC.KeepAlive(bw);

				sSaveTo.Close();

				m_xmlWriter = null;
			}
			catch(Exception)
			{
				sSaveTo.Close();
				m_xmlWriter = null;

				throw;
			}
		}

		private void WriteHeader(BinaryWriter bw)
		{
			Debug.Assert(bw != null);
			if(bw == null) throw new ArgumentNullException("br");

			bw.Write(MemUtil.UInt32ToBytes(FileSignature1));
			bw.Write(MemUtil.UInt32ToBytes(FileSignature2));
			bw.Write(MemUtil.UInt32ToBytes(FileVersion32));

			WriteHeaderField(bw, Kdb4HeaderFieldID.CipherID,
				m_pwDatabase.DataCipherUuid.UuidBytes);

			uint uCprID = (uint)m_pwDatabase.Compression;
			WriteHeaderField(bw, Kdb4HeaderFieldID.CompressionFlags, MemUtil.UInt32ToBytes(uCprID));

			WriteHeaderField(bw, Kdb4HeaderFieldID.MasterSeed, m_pbMasterSeed);
			WriteHeaderField(bw, Kdb4HeaderFieldID.TransformSeed, m_pbTransformSeed);
			WriteHeaderField(bw, Kdb4HeaderFieldID.TransformRounds, MemUtil.UInt64ToBytes(m_pwDatabase.KeyEncryptionRounds));

			WriteHeaderField(bw, Kdb4HeaderFieldID.EncryptionIV, m_pbEncryptionIV);
			WriteHeaderField(bw, Kdb4HeaderFieldID.ProtectedStreamKey, m_pbProtectedStreamKey);
			WriteHeaderField(bw, Kdb4HeaderFieldID.StreamStartBytes, m_pbStreamStartBytes);

			WriteHeaderField(bw, Kdb4HeaderFieldID.EndOfHeader, new byte[]{ (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });
			bw.Flush();
		}

		private static void WriteHeaderField(BinaryWriter bwOut,
			Kdb4HeaderFieldID kdbID, byte[] pbData)
		{
			Debug.Assert(bwOut != null);
			if(bwOut == null) throw new ArgumentNullException("bwOut");

			bwOut.Write((byte)kdbID);

			if(pbData != null)
			{
				ushort uLength = (ushort)pbData.Length;
				bwOut.Write(uLength);

				if(uLength > 0) bwOut.Write(pbData);
			}
			else bwOut.Write((ushort)0);
		}

		private Stream AttachStreamEncryptor(Stream s)
		{
			MemoryStream ms = new MemoryStream();

			Debug.Assert(m_pbMasterSeed.Length == 32);
			ms.Write(m_pbMasterSeed, 0, 32);

			byte[] pKey32 = m_pwDatabase.MasterKey.GenerateKey32(m_pbTransformSeed,
				m_pwDatabase.KeyEncryptionRounds).ReadData();
			if((pKey32 == null) || (pKey32.Length != 32))
				throw new SecurityException(KLRes.InvalidCompositeKey);
			ms.Write(pKey32, 0, 32);

			SHA256Managed sha256 = new SHA256Managed();
			byte[] aesKey = sha256.ComputeHash(ms.ToArray());
			
			ms.Close();
			Array.Clear(pKey32, 0, 32);

			ICipherEngine iEngine = CipherPool.GlobalPool.GetCipher(m_pwDatabase.DataCipherUuid);
			if(iEngine == null) throw new SecurityException(KLRes.FileUnknownCipher);
			return iEngine.EncryptStream(s, aesKey, this.m_pbEncryptionIV);
		}

		private void WriteDocument()
		{
			Debug.Assert(m_xmlWriter != null);
			if(m_xmlWriter == null) throw new InvalidOperationException();

			uint uNumGroups, uNumEntries, uCurEntry = 0;
			m_pwDatabase.RootGroup.GetCounts(true, out uNumGroups, out uNumEntries);

			m_xmlWriter.Formatting = Formatting.Indented;
			m_xmlWriter.IndentChar = '\t';
			m_xmlWriter.Indentation = 1;

			m_xmlWriter.WriteStartDocument(true);
			m_xmlWriter.WriteStartElement(ElemDocNode);

			WriteMeta();

			m_xmlWriter.WriteStartElement(ElemRoot);
			StartGroup(m_pwDatabase.RootGroup);

			Stack<PwGroup> groupStack = new Stack<PwGroup>();
			groupStack.Push(m_pwDatabase.RootGroup);

			GroupHandler gh = delegate(PwGroup pg)
			{
				Debug.Assert(pg != null);
				if(pg == null) throw new ArgumentNullException("pg");

				while(true)
				{
					if(pg.ParentGroup == groupStack.Peek())
					{
						groupStack.Push(pg);
						StartGroup(pg);
						break;
					}
					else
					{
						groupStack.Pop();
						if(groupStack.Count <= 0) return false;

						EndGroup();
					}
				}

				return true;
			};

			EntryHandler eh = delegate(PwEntry pe)
			{
				Debug.Assert(pe != null);
				WriteEntry(pe, false);

				++uCurEntry;
				if(m_slLogger != null)
					if(!m_slLogger.SetProgress((100 * uCurEntry) / uNumEntries))
						return false;

				return true;
			};

			if(!m_pwDatabase.RootGroup.TraverseTree(TraversalMethod.PreOrder, gh, eh))
				throw new InvalidOperationException();

			while(groupStack.Count > 1)
			{
				m_xmlWriter.WriteEndElement();
				groupStack.Pop();
			}

			EndGroup();

			WriteList(ElemDeletedObjects, m_pwDatabase.DeletedObjects);
			m_xmlWriter.WriteEndElement(); // Root

			m_xmlWriter.WriteEndElement(); // ElemDocNode
			m_xmlWriter.WriteEndDocument();
		}

		private void WriteMeta()
		{
			m_xmlWriter.WriteStartElement(ElemMeta);

			WriteObject(ElemGenerator, PwDatabase.LocalizedAppName); // Generator name
			WriteObject(ElemDbName, m_pwDatabase.Name);
			WriteObject(ElemDbDesc, m_pwDatabase.Description);
			WriteObject(ElemDbDefaultUser, m_pwDatabase.DefaultUserName);
			WriteObject(ElemDbMntncHistoryDays, m_pwDatabase.MaintenanceHistoryDays);

			WriteList(ElemMemoryProt, m_pwDatabase.MemoryProtection);

			WriteCustomIconList();

			WriteObject(ElemLastSelectedGroup, m_pwDatabase.LastSelectedGroup);
			WriteObject(ElemLastTopVisibleGroup, m_pwDatabase.LastTopVisibleGroup);

			m_xmlWriter.WriteEndElement();
		}

		private void StartGroup(PwGroup pg)
		{
			m_xmlWriter.WriteStartElement(ElemGroup);
			WriteObject(ElemUuid, pg.Uuid);
			WriteObject(ElemName, pg.Name);
			WriteObject(ElemIcon, (uint)pg.IconID);
			
			if(pg.CustomIconUuid != PwUuid.Zero)
				WriteObject(ElemCustomIconID, pg.CustomIconUuid);
			
			WriteList(ElemTimes, pg);
			WriteObject(ElemIsExpanded, pg.IsExpanded);
			WriteObject(ElemGroupDefaultAutoTypeSeq, pg.DefaultAutoTypeSequence);
			WriteObject(ElemLastTopVisibleEntry, pg.LastTopVisibleEntry);
		}

		private void EndGroup()
		{
			m_xmlWriter.WriteEndElement(); // Close group element
		}

		private void WriteEntry(PwEntry pe, bool bIsHistory)
		{
			Debug.Assert(pe != null); if(pe == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(ElemEntry);

			WriteObject(ElemUuid, pe.Uuid);
			WriteObject(ElemIcon, (uint)pe.IconID);
			
			if(pe.CustomIconUuid != PwUuid.Zero)
				WriteObject(ElemCustomIconID, pe.CustomIconUuid);

			WriteObject(ElemFgColor, StrUtil.ColorToUnnamedHtml(pe.ForegroundColor, true));
			WriteObject(ElemBgColor, StrUtil.ColorToUnnamedHtml(pe.BackgroundColor, true));
			WriteObject(ElemOverrideUrl, pe.OverrideUrl);

			WriteList(ElemTimes, pe);

			WriteList(pe.Strings, true);
			WriteList(pe.Binaries);
			WriteList(ElemAutoType, pe.AutoType);

			if(!bIsHistory) WriteList(ElemHistory, pe.History, true);
			else { Debug.Assert(pe.History.UCount == 0); }

			m_xmlWriter.WriteEndElement();
		}

		private void WriteList(ProtectedStringDictionary dictStrings, bool bEntryStrings)
		{
			Debug.Assert(dictStrings != null);
			if(dictStrings == null) throw new ArgumentNullException("dictStrings");

			foreach(KeyValuePair<string, ProtectedString> kvp in dictStrings)
				WriteObject(kvp.Key, kvp.Value, bEntryStrings);
		}

		private void WriteList(ProtectedBinaryDictionary dictBinaries)
		{
			Debug.Assert(dictBinaries != null);
			if(dictBinaries == null) throw new ArgumentNullException("dictBinaries");

			foreach(KeyValuePair<string, ProtectedBinary> kvp in dictBinaries)
				WriteObject(kvp.Key, kvp.Value);
		}

		private void WriteList(string name, AutoTypeConfig dictAutoType)
		{
			Debug.Assert(name != null);
			Debug.Assert(dictAutoType != null);
			if(dictAutoType == null) throw new ArgumentNullException("dictAutoType");

			m_xmlWriter.WriteStartElement(name);

			WriteObject(ElemAutoTypeEnabled, dictAutoType.Enabled);
			WriteObject(ElemAutoTypeObfuscation, (uint)dictAutoType.ObfuscationOptions);

			if(dictAutoType.DefaultSequence.Length > 0)
				WriteObject(ElemAutoTypeDefaultSeq, dictAutoType.DefaultSequence);

			foreach(KeyValuePair<string, string> kvp in dictAutoType.WindowSequencePairs)
				WriteObject(kvp);

			m_xmlWriter.WriteEndElement();
		}

		private void WriteList(string name, ITimeLogger times)
		{
			Debug.Assert(name != null);
			Debug.Assert(times != null); if(times == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(name);

			WriteObject(ElemLastModTime, times.LastModificationTime);
			WriteObject(ElemCreationTime, times.CreationTime);
			WriteObject(ElemLastAccessTime, times.LastAccessTime);
			WriteObject(ElemExpiryTime, times.ExpiryTime);
			WriteObject(ElemExpires, times.Expires);
			WriteObject(ElemUsageCount, times.UsageCount);

			m_xmlWriter.WriteEndElement(); // Name
		}

		private void WriteList(string name, PwObjectList<PwEntry> entryList, bool bIsHistory)
		{
			Debug.Assert(name != null);
			Debug.Assert(entryList != null); if(entryList == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(name);

			foreach(PwEntry pe in entryList)
				WriteEntry(pe, bIsHistory);

			m_xmlWriter.WriteEndElement();
		}

		private void WriteList(string name, PwObjectList<PwDeletedObject> deletedList)
		{
			Debug.Assert(name != null);
			Debug.Assert(deletedList != null); if(deletedList == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(name);

			foreach(PwDeletedObject pdo in deletedList)
				WriteObject(ElemDeletedObject, pdo);

			m_xmlWriter.WriteEndElement();
		}

		private void WriteList(string name, MemoryProtectionConfig value)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null);

			m_xmlWriter.WriteStartElement(name);

			WriteObject(ElemProtTitle, value.ProtectTitle);
			WriteObject(ElemProtUserName, value.ProtectUserName);
			WriteObject(ElemProtPassword, value.ProtectPassword);
			WriteObject(ElemProtURL, value.ProtectUrl);
			WriteObject(ElemProtNotes, value.ProtectNotes);
			WriteObject(ElemProtAutoHide, value.AutoEnableVisualHiding);

			m_xmlWriter.WriteEndElement();
		}

		private void WriteCustomIconList()
		{
			if(m_pwDatabase.CustomIcons.Count == 0) return;

			m_xmlWriter.WriteStartElement(ElemCustomIcons);

			foreach(PwCustomIcon pwci in m_pwDatabase.CustomIcons)
			{
				m_xmlWriter.WriteStartElement(ElemCustomIconItem);

				WriteObject(ElemCustomIconItemID, pwci.Uuid);

				string strData = Convert.ToBase64String(pwci.ImageDataPng);
				WriteObject(ElemCustomIconItemData, strData);

				m_xmlWriter.WriteEndElement();
			}

			m_xmlWriter.WriteEndElement();
		}

		private void WriteObject(string name, string value)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null);

			m_xmlWriter.WriteStartElement(name);
			m_xmlWriter.WriteString(value);
			m_xmlWriter.WriteEndElement();
		}

		private void WriteObject(string name, bool value)
		{
			Debug.Assert(name != null);

			WriteObject(name, value ? ValTrue : ValFalse);
		}

		private void WriteObject(string name, PwUuid value)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();

			WriteObject(name, Convert.ToBase64String(value.UuidBytes));
		}

		private void WriteObject(string name, uint value)
		{
			Debug.Assert(name != null);

			m_xmlWriter.WriteStartElement(name);
			m_xmlWriter.WriteString(value.ToString());
			m_xmlWriter.WriteEndElement();
		}

		private void WriteObject(string name, ulong value)
		{
			Debug.Assert(name != null);

			m_xmlWriter.WriteStartElement(name);
			m_xmlWriter.WriteString(value.ToString());
			m_xmlWriter.WriteEndElement();
		}

		private void WriteObject(string name, DateTime value)
		{
			Debug.Assert(name != null);

			WriteObject(name, value.ToString("s"));
		}

		private void WriteObject(KeyValuePair<string, string> kvp)
		{
			m_xmlWriter.WriteStartElement(ElemAutoTypeItem);

			m_xmlWriter.WriteStartElement(ElemWindow);
			m_xmlWriter.WriteString(kvp.Key);
			m_xmlWriter.WriteEndElement();
			m_xmlWriter.WriteStartElement(ElemKeystrokeSequence);
			m_xmlWriter.WriteString(kvp.Value);
			m_xmlWriter.WriteEndElement();

			m_xmlWriter.WriteEndElement(); // ElemKeyValuePair
		}

		private void WriteObject(string name, ProtectedString value, bool bIsEntryString)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(ElemString);
			m_xmlWriter.WriteStartElement(ElemKey);
			m_xmlWriter.WriteString(name);
			m_xmlWriter.WriteEndElement();
			m_xmlWriter.WriteStartElement(ElemValue);

			if(bIsEntryString)
			{
				if(name == PwDefs.TitleField)
					value.EnableProtection(m_pwDatabase.MemoryProtection.ProtectTitle);
				else if(name == PwDefs.UserNameField)
					value.EnableProtection(m_pwDatabase.MemoryProtection.ProtectUserName);
				else if(name == PwDefs.PasswordField)
					value.EnableProtection(m_pwDatabase.MemoryProtection.ProtectPassword);
				else if(name == PwDefs.UrlField)
					value.EnableProtection(m_pwDatabase.MemoryProtection.ProtectUrl);
				else if(name == PwDefs.NotesField)
					value.EnableProtection(m_pwDatabase.MemoryProtection.ProtectNotes);
			}

			if(value.IsProtected && (m_format != Kdb4Format.PlainXml))
			{
				m_xmlWriter.WriteAttributeString(AttrProtected, ValTrue);

				byte[] pbEncoded = value.ReadXorredString(m_randomStream);
				if(pbEncoded.Length > 0)
					m_xmlWriter.WriteBase64(pbEncoded, 0, pbEncoded.Length);
			}
			else
			{
				string strValue = value.ReadString();

				// If names should be localized, we need to apply the language-dependent
				// string transformation here. By default, language-dependent conversions
				// should be applied, otherwise characters could be rendered incorrectly
				// (code page problems).
				if(m_bLocalizedNames)
				{
					StringBuilder sb = new StringBuilder();
					foreach(char ch in strValue)
					{
						char chMapped = ch;

						// Symbols and surrogates must be moved into the correct code
						// page area
						if(char.IsSymbol(ch) || char.IsSurrogate(ch))
						{
							System.Globalization.UnicodeCategory cat = char.GetUnicodeCategory(ch);
							// Map character to correct position in code page
							chMapped = (char)((int)cat * 32 + ch);
						}
						else if(char.IsControl(ch))
						{
							if(ch >= 256) // Control character in high ANSI code page
							{
								// Some of the control characters map to corresponding ones
								// in the low ANSI range (up to 255) when calling
								// ToLower on them with invariant culture (see
								// http://lists.ximian.com/pipermail/mono-patches/2002-February/086106.html )
								chMapped = char.ToLower(ch, CultureInfo.InvariantCulture);
							}
						}

						sb.Append(chMapped);
					}

					strValue = sb.ToString(); // Correct string for current code page
				}

				m_xmlWriter.WriteString(strValue);
			}

			m_xmlWriter.WriteEndElement(); // ElemValue
			m_xmlWriter.WriteEndElement(); // ElemString
		}

		private void WriteObject(string name, ProtectedBinary value)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(ElemBinary);
			m_xmlWriter.WriteStartElement(ElemKey);
			m_xmlWriter.WriteString(name);
			m_xmlWriter.WriteEndElement();
			m_xmlWriter.WriteStartElement(ElemValue);

			if((value.IsProtected) && (m_format != Kdb4Format.PlainXml))
			{
				m_xmlWriter.WriteAttributeString(AttrProtected, ValTrue);

				byte[] pbEncoded = value.ReadXorredData(m_randomStream);
				if(pbEncoded.Length > 0)
					m_xmlWriter.WriteBase64(pbEncoded, 0, pbEncoded.Length);
			}
			else
			{
				byte[] pbRaw = value.ReadData();
				m_xmlWriter.WriteBase64(pbRaw, 0, pbRaw.Length);
			}

			m_xmlWriter.WriteEndElement(); // ElemValue
			m_xmlWriter.WriteEndElement(); // ElemBinary
		}

		private void WriteObject(string name, PwDeletedObject value)
		{
			Debug.Assert(name != null);
			Debug.Assert(value != null); if(value == null) throw new ArgumentNullException();

			m_xmlWriter.WriteStartElement(name);
			WriteObject(ElemUuid, value.Uuid);
			WriteObject(ElemDeletionTime, value.DeletionTime);
			m_xmlWriter.WriteEndElement();
		}

		/// <summary>
		/// Write entries to a stream.
		/// </summary>
		/// <param name="msOutput">Output stream to which the entries will be written.</param>
		/// <param name="pwDatabase">Source database.</param>
		/// <param name="vEntries">Entries to serialize.</param>
		/// <returns>Returns <c>true</c>, if the entries were written successfully to the stream.</returns>
		public static bool WriteEntries(Stream msOutput, PwDatabase pwDatabase, PwEntry[] vEntries)
		{
			Kdb4File f = new Kdb4File(pwDatabase);
			f.m_format = Kdb4Format.PlainXml;

			XmlTextWriter xtw = null;
			try { xtw = new XmlTextWriter(msOutput, Encoding.UTF8); }
			catch(Exception) { Debug.Assert(false); return false; }
			if(xtw == null) { Debug.Assert(false); return false; }

			f.m_xmlWriter = xtw;

			xtw.Formatting = Formatting.Indented;
			xtw.IndentChar = '\t';
			xtw.Indentation = 1;

			xtw.WriteStartDocument(true);
			xtw.WriteStartElement(ElemRoot);

			foreach(PwEntry pe in vEntries)
				f.WriteEntry(pe, false);

			xtw.WriteEndElement();
			xtw.WriteEndDocument();

			xtw.Flush();
			xtw.Close();
			return true;
		}
	}
}
