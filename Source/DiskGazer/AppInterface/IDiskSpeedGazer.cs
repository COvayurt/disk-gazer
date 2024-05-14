using DiskGazer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskGazer.AppInterface
{
    public interface IDiskSpeedGazer
    {
		DiskInfo FindTheFastestDisk();
	}
}
