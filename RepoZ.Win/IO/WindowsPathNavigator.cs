﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RepoZ.Api.IO;

namespace RepoZ.Win.IO
{
	public class WindowsPathNavigator : IPathNavigator
	{
		public void Navigate(string path)
		{
			try
			{
				Process.Start(path);
			}
			catch (Exception)
			{
				throw;
			}
		}
	}
}