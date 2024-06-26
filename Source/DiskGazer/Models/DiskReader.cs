﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

using DiskGazer.Models.Win32;

namespace DiskGazer.Models
{
	internal static class DiskReader
	{
		#region Native

		private const string NativeExeFileName = "DiskGazer.exe"; // Executable file of Win32 console application
		private static readonly string _nativeExeFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NativeExeFileName);

		/// <summary>
		/// Whether executable file exists
		/// </summary>
		internal static bool NativeExeExists => File.Exists(_nativeExeFilePath);

		private static Process _readProcess; // Process to run Win32 console application

		/// <summary>
		/// Reads disk by native (asynchronously and with cancellation).
		/// </summary>
		/// <param name="rawData">Raw data</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result data</returns>
		internal static async Task<RawData> ReadDiskNativeAsync(RawData rawData, CancellationToken cancellationToken)
		{
			var readTask = Task.Run(() => ReadDiskNative(rawData), cancellationToken);

			var tcs = new TaskCompletionSource<bool>();

			cancellationToken.Register(() =>
			{
				try
				{
					if ((_readProcess != null) && !_readProcess.HasExited)
						_readProcess.Kill();
				}
				catch (InvalidOperationException ioe)
				{
					// If the process has been disposed, this exception will be thrown.
					Debug.WriteLine($"There is no associated process.{Environment.NewLine}{ioe}");
				}

				tcs.SetCanceled();
			});

			var cancelTask = tcs.Task;

			var completedTask = await Task.WhenAny(readTask, cancelTask);
			if (completedTask == cancelTask)
				throw new OperationCanceledException("Read disk by native is canceled.");

			return await readTask;
		}

		/// <summary>
		/// Reads disk by native (synchronously).
		/// </summary>
		/// <param name="rawData">Raw data</param>
		/// <returns>Result data</returns>
		internal static RawData ReadDiskNative(RawData rawData)
		{
			if (!NativeExeExists)
			{
				rawData.Result = ReadResult.Failure;
				rawData.Message = $"Cannot find {NativeExeFileName}.";
				return rawData;
			}

			var blockOffsetMultiple = rawData.BlockOffsetMultiple;

			try
			{
				var arguments = string.Format("{0} {1} {2} {3} {4}",
					Settings.Current.PhysicalDrive,
					Settings.Current.BlockSize,
					Settings.Current.BlockOffset * blockOffsetMultiple,
					Settings.Current.AreaSize,
					Settings.Current.AreaLocation);

				if (Settings.Current.AreaRatioInner < Settings.Current.AreaRatioOuter)
				{
					arguments += string.Format(" {0} {1}",
						Settings.Current.AreaRatioInner,
						Settings.Current.AreaRatioOuter);
				}

				using (_readProcess = new Process
				{
					StartInfo =
					{
						FileName = _nativeExeFilePath,
						Verb = "RunAs", // Run as administrator.
						Arguments = arguments,
						UseShellExecute = false,
						CreateNoWindow = true,
						//WindowStyle = ProcessWindowStyle.Hidden,
						RedirectStandardOutput = true,
					}
				})
				{
					_readProcess.Start();
					var outcome = _readProcess.StandardOutput.ReadToEnd();
					_readProcess.WaitForExit();

					rawData.Result = (_readProcess.HasExited & (_readProcess.ExitCode == 0))
						? ReadResult.Success
						: ReadResult.Failure;

					rawData.Outcome = outcome;

					switch (rawData.Result)
					{
						case ReadResult.Success:
							rawData.Data = FindData(outcome);
							break;
						case ReadResult.Failure:
							rawData.Message = FindMessage(outcome);
							break;
					}
				}
			}
			catch (Exception ex)
			{
				rawData.Result = ReadResult.Failure;
				rawData.Message = $"Failed to execute {NativeExeFileName}. {ex.Message}";
			}

			return rawData;
		}

		private static double[] FindData(string outcome)
		{
			const string startSign = "[Start data]";
			const string endSign = "[End data]";

			var startPoint = outcome.IndexOf(startSign, StringComparison.InvariantCulture);
			var endPoint = outcome.LastIndexOf(endSign, StringComparison.InvariantCulture);

			if ((startPoint < 0) || (endPoint < 0) || (startPoint >= endPoint))
				return null;

			return outcome
				.Substring(startPoint + startSign.Length, (endPoint - 1) - (startPoint + startSign.Length))
				.Split() // Split() is more robust than Split(new[] { " ", Environment.NewLine })
				.Where(x => !string.IsNullOrEmpty(x))
				.Select(x => double.Parse(x, NumberStyles.Any, CultureInfo.InvariantCulture)) // Culture matters.
				.ToArray();
		}

		private static string FindMessage(string outcome)
		{
			var lines = outcome.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

			return (lines.Length >= 1)
				? lines[lines.Length - 1] // Last line should contain error message.
				: string.Empty;
		}

		#endregion

		#region P/Invoke

		/// <summary>
		/// Reads disk by P/Invoke (asynchronously and with cancellation).
		/// </summary>
		/// <param name="rawData">Raw data</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result data</returns>
		internal static Task<RawData> ReadDiskPInvokeAsync(RawData rawData, CancellationToken cancellationToken)
		{
			return Task.Run(() => ReadDiskPInvoke(rawData, cancellationToken), cancellationToken);
		}

		/// <summary>
		/// Reads disk by P/Invoke (synchronously and with cancellation).
		/// </summary>
		/// <param name="rawData">Raw data</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result data</returns>
		internal static RawData ReadDiskPInvoke(RawData rawData, CancellationToken cancellationToken)
		{
			var blockOffsetMultiple = rawData.BlockOffsetMultiple;

			SafeFileHandle hFile = null;

			try
			{
				// ----------
				// Read disk.
				// ----------
				// This section is based on sequential read test of CrystalDiskMark (3.0.2)
				// created by hiyohiyo (http://crystalmark.info/).

				// Get handle to disk.
				hFile = NativeMethod.CreateFile(
					@$"\\.\PhysicalDrive{Settings.Current.PhysicalDrive}",
					NativeMethod.GENERIC_READ, // Administrative privilege is required.
					0,
					IntPtr.Zero,
					NativeMethod.OPEN_EXISTING,
					NativeMethod.FILE_ATTRIBUTE_NORMAL | NativeMethod.FILE_FLAG_NO_BUFFERING | NativeMethod.FILE_FLAG_SEQUENTIAL_SCAN,
					IntPtr.Zero);
				if (hFile is null || hFile.IsInvalid)
				{
					// This is normal when this application is not run by administrator.
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get handle to disk.");
				}

				// Prepare parameters.
				var areaSizeActual = Settings.Current.AreaSize; // Area size for actual reading (MiB)
				if (0 < Settings.Current.BlockOffset)
					areaSizeActual -= 1; // 1 is for the last MiB of area. If offset, it may exceed disk size.

				int readNumber = (areaSizeActual * 1024) / Settings.Current.BlockSize; // The number of reads

				int loopOuter = 1; // The number of outer loops
				int loopInner = readNumber; // The number of inner loops

				if (Settings.Current.AreaRatioInner < Settings.Current.AreaRatioOuter)
				{
					loopOuter = (areaSizeActual * 1024) / (Settings.Current.BlockSize * Settings.Current.AreaRatioOuter);
					loopInner = Settings.Current.AreaRatioInner;

					readNumber = loopInner * loopOuter;
				}

				var areaLocationBytes = (long)Settings.Current.AreaLocation * 1024L * 1024L; // Bytes
				var blockOffsetBytes = (long)Settings.Current.BlockOffset * (long)blockOffsetMultiple * 1024L; // Bytes
				var jumpBytes = (long)Settings.Current.BlockSize * (long)Settings.Current.AreaRatioOuter * 1024L; // Bytes

				areaLocationBytes += blockOffsetBytes;

				var bufferSize = (uint)Settings.Current.BlockSize * 1024U; // Buffer size (Bytes)
				var buffer = new byte[bufferSize]; // Buffer
				uint readSize = 0U;

				var sw = new Stopwatch();
				var lapTime = new TimeSpan[readNumber + 1]; // 1 is for leading zero time.
				lapTime[0] = TimeSpan.Zero; // Leading zero time

				for (int i = 0; i < loopOuter; i++)
				{
					if (0 < i)
						areaLocationBytes += jumpBytes;

					// Move pointer.
					var result1 = NativeMethod.SetFilePointerEx(
						hFile,
						areaLocationBytes,
						IntPtr.Zero,
						NativeMethod.FILE_BEGIN);
					if (result1 == false)
						throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to move pointer.");

					// Measure disk transfer rate (sequential read).
					for (int j = 1; j <= loopInner; j++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						sw.Start();

						var result2 = NativeMethod.ReadFile(
							hFile,
							buffer,
							bufferSize,
							ref readSize,
							IntPtr.Zero);
						if (result2 == false)
							throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to measure disk transfer rate.");

						sw.Stop();

						lapTime[i * loopInner + j] = sw.Elapsed;
					}
				}

				cancellationToken.ThrowIfCancellationRequested();

				// ----------------
				// Process results.
				// ----------------
				// Calculate each transfer rate.
				var data = new double[readNumber];

				for (int i = 1; i <= readNumber; i++)
				{
					var timeEach = (lapTime[i] - lapTime[i - 1]).TotalSeconds; // Second
					var scoreEach = Math.Floor(bufferSize / timeEach) / 1000000D; // MB/s

					data[i - 1] = scoreEach;
				}

				// Calculate total transfer rate (just for reference).
				var totalTime = lapTime[readNumber].TotalSeconds; // Second
				var totalRead = (double)Settings.Current.BlockSize * (double)readNumber * 1024D; // Bytes

				var totalScore = Math.Floor(totalRead / totalTime) / 1000000D; // MB/s

				// Compose outcome.
				var outcome = new StringBuilder();
				outcome.AppendLine("[Start data]");

				int k = 0;
				for (int i = 0; i < readNumber; i++)
				{
					outcome.Append($"{data[i]:f6} "); // Data have 6 decimal places.

					k++;
					if ((k == 6) | (i == readNumber - 1))
					{
						k = 0;
						outcome.AppendLine();
					}
				}

				outcome.AppendLine("[End data]");
				outcome.Append($"Total {totalScore:f6} MB/s");

				rawData.Result = ReadResult.Success;
				rawData.Outcome = outcome.ToString();
				rawData.Data = data;
			}
			catch (Win32Exception ex)
			{
				rawData.Result = ReadResult.Failure;
				rawData.Message = $"{ex.Message.Substring(0, ex.Message.Length - 1)} (Code: {ex.ErrorCode}).";
			}
			catch (Exception ex) // Including OperationCanceledException
			{
				rawData.Result = ReadResult.Failure;
				rawData.Message = ex.Message;
			}
			finally
			{
				if (hFile is not null)
				{
					// CloseHandle is inappropriate to close SafeFileHandle.
					// Dispose method is not necessary because Close method will call it internally.
					hFile.Close();
				}
			}

			return rawData;
		}

		#endregion
	}
}