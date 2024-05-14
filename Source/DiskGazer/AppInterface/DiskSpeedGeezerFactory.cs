using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskGazer.AppInterface
{
    public class DiskSpeedGeezerFactory
    {
		public static IDiskSpeedGazer getDiskSpeedGazerInstance()
		{
			return new DiskSpeedGazer();
		}
    }
}
