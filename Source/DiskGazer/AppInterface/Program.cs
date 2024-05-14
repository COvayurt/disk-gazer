using DiskGazer.AppInterface;
using DiskGazer.Common;
using DiskGazer.Models;
using DiskGazer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskGazer.App
{
	internal class Program
	{
		static void Main(string[] args)
		{
			DiskSpeedGazer diskSpeedGazer = new DiskSpeedGazer();
			DiskInfo diskInfo = diskSpeedGazer.FindTheFastestDisk();
			Console.WriteLine("Fastest Disk is : " + diskInfo.Name);
			Console.WriteLine("Press key to continue...");
			Console.ReadLine();
		}
	}
}
