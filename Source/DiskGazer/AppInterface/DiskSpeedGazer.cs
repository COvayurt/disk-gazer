using DiskGazer.Models;
using DiskGazer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskGazer.AppInterface
{
	internal class DiskSpeedGazer : IDiskSpeedGazer
	{
		public DiskInfo FindTheFastestDisk()
		{
			MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
			Task task = mainWindowViewModel.InitializeAsync();
			task.Wait();
			ObservableCollection<string> disks = mainWindowViewModel.DiskRosterNames;

			int diskCount = 0;
			DiskInfo fastestDisk = mainWindowViewModel.CurrentDisk;
			double fastestAverageDiskScore = 0.0;
			foreach (string disk in disks)
			{
				Settings.Current.PhysicalDrive = diskCount;
				mainWindowViewModel.DiskRosterNamesIndex = diskCount;

				Console.WriteLine("Processing the " + disk + "(" + diskCount + ")");
				Task runExecuteCommandTask = mainWindowViewModel.RunExecuteCommand();
				runExecuteCommandTask.Wait();

				if (fastestAverageDiskScore.CompareTo(mainWindowViewModel.ScoreAvg) < 0)
				{
					fastestAverageDiskScore = mainWindowViewModel.ScoreAvg;
					fastestDisk = mainWindowViewModel.CurrentDisk;
					//Console.WriteLine("Current Fastest Disk is :" + fastestDisk.Name + " and its score is : " + fastestAverageDiskScore);
				}
				Console.WriteLine("Current Disk is :" + disk + " and its score is : " + mainWindowViewModel.ScoreAvg);
				mainWindowViewModel.RunClearLinesExecute();
				diskCount++;
			}

			Debug.WriteLine("Fastest disk is:" + fastestDisk.Name + " Press key to continue..");

			return fastestDisk;
		}
	}
}
