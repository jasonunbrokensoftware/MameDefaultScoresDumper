﻿//
// MAME Default Scores Dumper
// Copyright (c) Jason Carr 2020. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
//

namespace MameDefaultScoresDumper
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Windows.Forms;

    public class Program
    {
        private static int successCount, missingCount, errorCount;

        public static void Main(string[] args)
        {
            try
            {
                string mameExePath = Path.Combine(Application.StartupPath, "MAME", "Mame64.exe");
                string hi2TxtFolderPath = Path.Combine(Application.StartupPath, "hi2txt");
                string sevenZipExePath = Path.Combine(Application.StartupPath, "7-Zip", "7z.exe");
                string hi2TxtExePath = Path.Combine(hi2TxtFolderPath, "hi2txt.exe");
                string hi2TxtZipPath = Path.Combine(hi2TxtFolderPath, "hi2txt.zip");
                string resultsFolder = Path.Combine(Application.StartupPath, "Results");
                string datPath = Path.Combine(Path.GetDirectoryName(mameExePath), "plugins", "hiscore", "hiscore.dat");
                string hiFolderPath = Path.Combine(Path.GetDirectoryName(mameExePath), "hi");

                Console.WriteLine();
                Console.WriteLine($"MAME Default Scores Dumper v{Assembly.GetExecutingAssembly().GetName().Version}\nBy Jason Carr, developer of LaunchBox - https://www.launchbox-app.com/\n");
                Console.WriteLine("Thanks to GreatStone and community for hi2txt!\n");
                Console.WriteLine("Recommandation: starts with a delay of 15, then try again with 30, then 180, to analyze a full set as quickly as possible.\n");

                bool validArgs = true;
                bool textMode = false;
                int secondsDelay = 30;

                if (args.Length == 2)
                {
                    if (args[0] == "-txt")
                    {
                        textMode = true;
                    }
                    else if (args[0] != "-xml")
                    {
                        validArgs = false;
                    }

                    if (!int.TryParse(args[1], out secondsDelay))
                    {
                        validArgs = false;
                    }
                }
                else
                {
                    validArgs = false;
                }

                if (!validArgs)
                {
                    Console.WriteLine("Incorrect usage. Proper usage: MameDefaultScoresDumper.exe [-txt or -xml] [seconds to wait per game]\n");
                    Console.WriteLine("Examples:\n");
                    Console.WriteLine("MameDefaultScoresDumper.exe -xml 30");
                    Console.WriteLine("MameDefaultScoresDumper.exe -txt 240\n");
                    return;
                }

                if (!File.Exists(mameExePath) || !File.Exists(datPath))
                {
                    Console.WriteLine("ERROR: MAME 64 files not found in MAME subfolder; place the latest version of MAME 64-bit into the MAME subfolder.\nMame64.exe needs to be directly inside of this folder.\n\nDo not overwrite the existing MAME configuration files when copying MAME. However, you may need to edit the mame.ini\nfile to set the path to your ROMs (rompath setting).");
                    return;
                }

                if (!File.Exists(hi2TxtExePath))
                {
                    Console.WriteLine("ERROR: Missing hi2txt\\hi2txt.exe file. Cannot continue.");
                    return;
                }

                if (!File.Exists(hi2TxtZipPath))
                {
                    Console.WriteLine("ERROR: Missing hi2txt\\hi2txt.zip file. Cannot continue.");
                    return;
                }

                if (!File.Exists(sevenZipExePath))
                {
                    Console.WriteLine("ERROR: Missing 7-Zip\\7z.exe file. Cannot continue.");
                    return;
                }

                Directory.SetCurrentDirectory(Path.GetDirectoryName(mameExePath));

                var romNames = Program.GetSevenZipFilePaths(hi2TxtZipPath, sevenZipExePath)
                    .Where(n => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !n.Equals("_template.xml"))
                    .Select(n => n.Replace(".xml", string.Empty))
                    .OrderBy(n => n)
                    .ToList();

                string resultsSuccessFolder = Path.Combine(resultsFolder, "Success");
                string resultsMissingFolder = Path.Combine(resultsFolder, "Missing");
                string resultsErrorFolder = Path.Combine(resultsFolder, "Error");

                if (!Directory.Exists(resultsSuccessFolder))
                {
                    Directory.CreateDirectory(resultsSuccessFolder);
                }

                if (!Directory.Exists(resultsMissingFolder))
                {
                    Directory.CreateDirectory(resultsMissingFolder);
                }

                if (!Directory.Exists(resultsErrorFolder))
                {
                    Directory.CreateDirectory(resultsErrorFolder);
                }

                foreach (string romName in romNames)
                {
                    string resultPath = Path.Combine(resultsSuccessFolder, romName + (textMode ? ".txt" : ".xml"));
                    string resultMissingPath = Path.Combine(resultsMissingFolder, romName + ".txt");
                    string resultErrorPath = Path.Combine(resultsErrorFolder, romName + ".txt");

                    if (File.Exists(resultPath) || File.Exists(resultMissingPath) || File.Exists(resultErrorPath))
                    {
                        Console.WriteLine($"Skipping {romName} because a results file already exists.\n");
                        continue;
                    }

                    Console.WriteLine($"Launching MAME to create {romName} files... [{mameExePath} -bench {secondsDelay} {romName}]");

                    var mameProcess = new Process();
                    mameProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    mameProcess.StartInfo.FileName = mameExePath;
                    mameProcess.StartInfo.Arguments = "-bench " + secondsDelay.ToString() + " " + romName;
                    mameProcess.Start();
                    mameProcess?.WaitForExit();

                    Console.WriteLine($"Attempting to dump {romName} high scores from hiscore file...");
                    Program.DumpHi2Txt(romName, hi2TxtExePath, Path.Combine(hiFolderPath, romName), datPath, resultPath, resultMissingPath, resultErrorPath, textMode);
                }

                Console.WriteLine($"Process completed.\nTotal ROMs Processed: {romNames.Count()}\nROMs Successfully Parsed: {Program.successCount}\nROMs Missing hiscore/nvram: {Program.missingCount}\nROMs Errored: {Program.errorCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nUnexpected Error: {ex}");
            }
        }

        private static void DumpHi2Txt(string romName, string hi2TxtPath, string hiPath, string datPath, string destinationPath, string missingDestinationPath, string errorDestinationPath, bool textMode)
        {
            var info = new ProcessStartInfo(
                hi2TxtPath,
                string.Format(CultureInfo.InvariantCulture, "{0}-r \"{1}\" -hiscoredat \"{2}\"", textMode ? string.Empty : "-xml ", hiPath, datPath))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process
            {
                StartInfo = info,
                EnableRaisingEvents = true
            };

            var builder = new StringBuilder();

            process.ErrorDataReceived += (sender, args) => builder.AppendLine(args.Data);
            process.OutputDataReceived += (sender, args) => builder.AppendLine(args.Data);

            process.Exited += (sender, args) =>
            {
                string results = builder.ToString();

                bool missing = results.StartsWith("ERROR: No hiscores file found", StringComparison.OrdinalIgnoreCase);
                if (missing)
                {
                    Program.missingCount++;
                    File.WriteAllText(missingDestinationPath, "No hiscore file or nvram directory could be found to parse.");
                }
                else
                {
                    bool error = results.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                    if (error)
                    {
                        Program.errorCount++;
                        File.WriteAllText(errorDestinationPath, results);
                        Console.WriteLine($"Dump errored out for {romName}.\n");
                    }
                    else
                    {
                        Program.successCount++;
                        File.WriteAllText(destinationPath, results);
                        Console.WriteLine($"High scores for {romName} dumped successfully.\n");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }

        private static IEnumerable<string> GetSevenZipFilePaths(string archiveFilePath, string sevenZipExePath)
        {
            var info = new ProcessStartInfo
            {
                FileName = sevenZipExePath,
                Arguments = $"l \"{archiveFilePath}\" -slt",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = new Process())
            {
                process.StartInfo = info;
                process.EnableRaisingEvents = true;

                var outputLines = new ConcurrentBag<string>();

                process.OutputDataReceived += (sender, args) =>
                {
                    outputLines.Add(args.Data);
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    outputLines.Add(args.Data);
                };

                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();

                return process.ExitCode != 0 ? null : outputLines.Where(l => l != null && l.StartsWith("Path = ") && l.Length > 7 && !string.Equals(l.Substring(7), archiveFilePath, StringComparison.OrdinalIgnoreCase)).Select(line => line.Substring(7));
            }
        }
    }
}