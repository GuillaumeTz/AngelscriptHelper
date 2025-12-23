using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;

namespace AngelScriptHelper
{
	class CErrorListHelper
	{
		private readonly ErrorListProvider _errorListProvider;

		private static CErrorListHelper mInstance = null;

		public CErrorListHelper(IServiceProvider serviceProvider)
		{
			_errorListProvider = new ErrorListProvider(serviceProvider);
		}

		public static void Init(IServiceProvider ServiceProvider)
		{	
			if (mInstance == null)
				mInstance = new CErrorListHelper(ServiceProvider);
		}

		public static CErrorListHelper Instance()
		{
			return mInstance;
		}

		public void ShowErrorList()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			_errorListProvider.Show();
		}

		public void AddError(string message, string filePath, int line, int column)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			// Create a new error task item
			ErrorTask errorTask = new ErrorTask
			{
				Category = TaskCategory.BuildCompile,  // Or TaskCategory.CodeSense for real-time analysis
				ErrorCategory = TaskErrorCategory.Error, // Can be Warning or Message
				Text = message,
				Document = filePath,
				Line = line - 1,  // Line index is zero-based
				Column = column - 1
			};

			// Set navigation action when the user double-clicks the error
			errorTask.Navigate += (s, e) =>
			{
				NavigateToFile(filePath, line, column);
			};

			// Add the error task to the provider
			_errorListProvider.Tasks.Add(errorTask);
		}

		public void ClearErrors()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			_errorListProvider.Tasks.Clear();
		}

		private void NavigateToFile(string filePath, int line, int column)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			// Ensure file exists
			if (!System.IO.File.Exists(filePath))
				return;

			// Open the document in Visual Studio
			VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath, Guid.Empty, out IVsUIHierarchy hierarchy, out uint itemID, out IVsWindowFrame windowFrame);

			if (windowFrame != null)
			{
				windowFrame.Show(); // Bring the document to the foreground

				// Get the IVsTextView to move the cursor
				MoveCursorWithDTE(line, column);
			}
		}
		private static void MoveCursorWithDTE(int line, int column)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			try
			{
				DTE2 dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;

				// Get the active document selection
				TextSelection selection = (TextSelection)dte.ActiveDocument.Selection;
				selection?.MoveToLineAndOffset(line, column + 1);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("MoveCursorWithDTE failed: " + ex.Message);
			}
		}
	}
}