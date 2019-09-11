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
using System.Xml;
using System.IO;
using System.IO.Compression;

namespace TrlUtil
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			if((args == null) || (args.Length != 2))
			{
				Console.WriteLine("Invalid or no arguments!");
				return;
			}

			try { ExecuteCmd(args[0], args[1]); }
			catch(Exception exCmd)
			{
				Console.WriteLine(exCmd.Message);
			}
		}

		private static void ExecuteCmd(string strCmd, string strFile)
		{
			if(strCmd == "convert_resx")
			{
				StreamWriter swOut = new StreamWriter(strFile + ".lng.xml",
					false, Encoding.UTF8);

				XmlDocument xmlIn = new XmlDocument();
				xmlIn.Load(strFile);

				foreach(XmlNode xmlChild in xmlIn.DocumentElement.ChildNodes)
				{
					if(xmlChild.Name != "data") continue;

					swOut.Write("<Data Name=\"" + xmlChild.Attributes["name"].Value +
						"\">\r\n\t<Value>" + xmlChild.SelectSingleNode("value").InnerXml +
						"</Value>\r\n</Data>\r\n");
				}

				swOut.Close();
			}
			else if(strCmd == "compress")
			{
				byte[] pbData = File.ReadAllBytes(strFile);

				FileStream fs = new FileStream(strFile + ".lngx", FileMode.Create,
					FileAccess.Write, FileShare.None);
				GZipStream gz = new GZipStream(fs, CompressionMode.Compress);

				gz.Write(pbData, 0, pbData.Length);
				gz.Close();
				fs.Close();
			}
			else if(strCmd == "src_from_xml")
			{
				XmlDocument xmlIn = new XmlDocument();
				xmlIn.Load(strFile);

				foreach(XmlNode xmlTable in xmlIn.DocumentElement.SelectNodes("StringTable"))
				{
					StreamWriter swOut = new StreamWriter(xmlTable.Attributes["Name"].Value +
						".Generated.cs", false, Encoding.UTF8);

					swOut.WriteLine("// This is a generated file!");
					swOut.WriteLine("// Do not edit manually, changes will be overwritten.");
					swOut.WriteLine();
					swOut.WriteLine("using System;");
					swOut.WriteLine("using System.Collections.Generic;");
					swOut.WriteLine();
					swOut.WriteLine("namespace " + xmlTable.Attributes["Namespace"].Value);
					swOut.WriteLine("{");
					swOut.WriteLine("\t/// <summary>");
					swOut.WriteLine("\t/// A strongly-typed resource class, for looking up localized strings, etc.");
					swOut.WriteLine("\t/// </summary>");
					swOut.WriteLine("\tpublic static class " + xmlTable.Attributes["Name"].Value);
					swOut.WriteLine("\t{");

					swOut.WriteLine("\t\tprivate static string TryGetEx(Dictionary<string, string> dictNew,");
					swOut.WriteLine("\t\t\tstring strName, string strDefault)");
					swOut.WriteLine("\t\t{");
					swOut.WriteLine("\t\t\tstring strTemp;");
					swOut.WriteLine();
					swOut.WriteLine("\t\t\tif(dictNew.TryGetValue(strName, out strTemp))");
					swOut.WriteLine("\t\t\t\treturn strTemp;");
					swOut.WriteLine();
					swOut.WriteLine("\t\t\treturn strDefault;");
					swOut.WriteLine("\t\t}");
					swOut.WriteLine();

					swOut.WriteLine("\t\tpublic static void SetTranslatedStrings(Dictionary<string, string> dictNew)");
					swOut.WriteLine("\t\t{");
					swOut.WriteLine("\t\t\tif(dictNew == null) throw new ArgumentNullException(\"dictNew\");");
					swOut.WriteLine();

					foreach(XmlNode xmlData in xmlTable.SelectNodes("Data"))
					{
						string strName = xmlData.Attributes["Name"].Value;

						swOut.WriteLine("\t\t\tm_str" + strName +
							" = TryGetEx(dictNew, \"" + strName +
							"\", m_str" + strName + ");");
					}

					swOut.WriteLine("\t\t}");

					foreach(XmlNode xmlData in xmlTable.SelectNodes("Data"))
					{
						string strName = xmlData.Attributes["Name"].Value;
						string strValue = xmlData.SelectSingleNode("Value").InnerText;
						if(strValue.Contains("\""))
						{
							Console.WriteLine(strValue);
							strValue = strValue.Replace("\"", "\"\"");
						}

						swOut.WriteLine();
						swOut.WriteLine("\t\tprivate static string m_str" +
							strName + " =");
						swOut.WriteLine("\t\t\t@\"" + strValue + "\";");

						swOut.WriteLine("\t\t/// <summary>");
						swOut.WriteLine("\t\t/// Look up a localized string similar to");
						swOut.WriteLine("\t\t/// '" + strValue + "'.");
						swOut.WriteLine("\t\t/// </summary>");
						swOut.WriteLine("\t\tpublic static string " +
							strName);
						swOut.WriteLine("\t\t{");
						swOut.WriteLine("\t\t\tget { return m_str" + strName +
							"; }");
						swOut.WriteLine("\t\t}");
					}

					swOut.WriteLine("\t}"); // Close class
					swOut.WriteLine("}");

					swOut.Close();
				}
			}
		}
	}
}
