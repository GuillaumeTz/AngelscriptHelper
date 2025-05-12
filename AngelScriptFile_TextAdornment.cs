using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AngelScriptHelper
{
	/// <summary>
	/// TextAdornment1 places red boxes behind all the "a"s in the editor window
	/// </summary>
	internal sealed class AngelScriptFile_TextAdornment
	{
		/// <summary>
		/// The layer of the adornment.
		/// </summary>
		private readonly IAdornmentLayer layer;

		/// <summary>
		/// Text view where the adornment is created.
		/// </summary>
		private readonly IWpfTextView view;

		/// <summary>
		/// Adornment brush.
		/// </summary>
		private readonly Brush ErrorBrush;

		private readonly Brush SuccessBrush;

		/// <summary>
		/// Adornment pen.
		/// </summary>
		private readonly Pen ErrorPen;

		public string FilePath;
		private bool bDiagnosticDirty = false;
		private object TagCompileSuccessObject = new object();

		private object TagErrors = new object();

		/// <summary>
		/// Initializes a new instance of the <see cref="AngelScriptFile_TextAdornment"/> class.
		/// </summary>
		/// <param name="view">Text view to create the adornment for</param>
		public AngelScriptFile_TextAdornment(IWpfTextView view, string InFilePath)
		{
			FilePath = InFilePath;

			if (view == null)
			{
				throw new ArgumentNullException("view");
			}

			// Get the ITextBuffer from the IWpfTextView
			this.view = view;
			this.view.TextBuffer.Changed += OnTextBufferChanged;

			this.layer = this.view.GetAdornmentLayer("AngelScriptFile_TextAdornment");

			this.view.LayoutChanged += this.OnLayoutChanged;

			// Create the pen and brush to color the box behind the a's
			this.ErrorBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0xff));
			this.ErrorBrush.Freeze();

			this.SuccessBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0xff, 0x00));
			this.SuccessBrush.Freeze();

			var penBrush = new SolidColorBrush(Colors.Red);
			penBrush.Freeze();
			this.ErrorPen = new Pen(penBrush, 0.5);
			this.ErrorPen.Freeze();

			CAngelScriptManager.Instance().OnDiagnosticsChanged += (s, e) =>
			{
				if (bDiagnosticDirty)
					return;

				bDiagnosticDirty = true;
				this.view.VisualElement.Dispatcher.Invoke(() =>
				{
					this.view.VisualElement.InvalidateVisual();
					this.view.VisualElement.UpdateLayout();
				});
			};
		}
		private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
		{
			this.layer.RemoveAdornmentsByTag(TagCompileSuccessObject);
		}

		/// <summary>
		/// Handles whenever the text displayed in the view changes by adding the adornment to any reformatted lines
		/// </summary>
		/// <remarks><para>This event is raised whenever the rendered text displayed in the <see cref="ITextView"/> changes.</para>
		/// <para>It is raised whenever the view does a layout (which happens when DisplayTextLineContainingBufferPosition is called or in response to text or classification changes).</para>
		/// <para>It is also raised whenever the view scrolls horizontally or when its size changes.</para>
		/// </remarks>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The event arguments.</param>
		internal void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
		{
			if (bDiagnosticDirty && e.NewOrReformattedLines.Count == 0)
			{
				bDiagnosticDirty = false;
				this.layer.RemoveAdornmentsByTag(TagErrors);
				this.layer.RemoveAdornmentsByTag(TagCompileSuccessObject);

				bool bHasMesssage = CAngelScriptManager.Instance().mDebugClient.DiagnosticsMessageMap.TryGetValue(FilePath, out CDiagnosticsMessage DiagnosticMessage);
				if (!bHasMesssage)
					return;

				if (DiagnosticMessage.Diagnostics.Count == 0)
				{
					Rect viewportRect = new Rect(0, 0, this.view.ViewportWidth, this.view.ViewportHeight);
					Geometry viewportGeometry = new RectangleGeometry(viewportRect);

					if (viewportGeometry != null)
					{
						var drawing = new GeometryDrawing(this.SuccessBrush, this.ErrorPen, viewportGeometry);
						drawing.Freeze();

						var drawingImage = new DrawingImage(drawing);
						drawingImage.Freeze();

						var image = new Image
						{
							Source = drawingImage,
						};

						// Align the image with the top of the bounds of the text geometry
						Canvas.SetLeft(image, viewportGeometry.Bounds.Left);
						Canvas.SetTop(image, viewportGeometry.Bounds.Top);

						this.layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, TagCompileSuccessObject, image, null);
					}
					return;
				}

				for (int LineNumber = 0; LineNumber < e.NewSnapshot.LineCount; ++LineNumber)
				{
					this.CreateVisuals(LineNumber);
				}
				return;
			}

			{
				bool bHasMesssage = CAngelScriptManager.Instance().mDebugClient.DiagnosticsMessageMap.TryGetValue(FilePath, out CDiagnosticsMessage DiagnosticMessage);
				if (!bHasMesssage || DiagnosticMessage.Diagnostics.Count == 0)
					return;

				foreach (ITextViewLine line in e.NewOrReformattedLines)
				{
					this.CreateVisuals(line.Start.GetContainingLineNumber());
				}
			}
		}

		private void CreateVisuals(int LineNumber)
		{
			bool bHasMesssage = CAngelScriptManager.Instance().mDebugClient.DiagnosticsMessageMap.TryGetValue(FilePath, out CDiagnosticsMessage DiagnosticMessage);
			if (!bHasMesssage || DiagnosticMessage.Diagnostics.Count == 0)
				return;

			IWpfTextViewLineCollection textViewLines = this.view.TextViewLines;
			string AllMessages = string.Empty;
			SnapshotSpan? span = null;

			bool bAlreadyCreatedVisual = false;
			foreach (CAngelscriptDiagnostic Diagnostic in DiagnosticMessage.Diagnostics)
			{
				if (LineNumber != Diagnostic.Start.LineNumber)
					continue;

				AllMessages += Diagnostic.message + "   ";

				if (bAlreadyCreatedVisual)
					continue;

				var StartLine = this.view.TextSnapshot.GetLineFromLineNumber(Diagnostic.Start.LineNumber);
				var EndLine = this.view.TextSnapshot.GetLineFromLineNumber(Diagnostic.End.LineNumber);

				span = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(StartLine.Start.Position + Diagnostic.Start.CharacterInLine, EndLine.Start.Position + Math.Min(EndLine.Length, Diagnostic.End.CharacterInLine)));

				Geometry geometry = textViewLines.GetMarkerGeometry(span.Value);
				if (geometry != null)
				{
					var drawing = new GeometryDrawing(this.ErrorBrush, this.ErrorPen, geometry);
					drawing.Freeze();

					var drawingImage = new DrawingImage(drawing);
					drawingImage.Freeze();

					var image = new Image
					{
						Source = drawingImage,
					};

					// Align the image with the top of the bounds of the text geometry
					Canvas.SetLeft(image, geometry.Bounds.Left);
					Canvas.SetTop(image, geometry.Bounds.Top);

					this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, TagErrors, image, null);
					bAlreadyCreatedVisual = true;
				}
			}

			if (span.HasValue)
			{
				Geometry geometry = textViewLines.GetMarkerGeometry(span.Value);
				if (geometry != null)
				{

					// Create a label with the text adornment
					TextBlock adornmentText = new TextBlock
					{
						Text = AllMessages,
						Foreground = Brushes.Red,
						Background = Brushes.Transparent,
						Padding = new Thickness(5),
						FontSize = 12
					};

					// Measure the adornment width and set the position at the end of the line
					Canvas.SetLeft(adornmentText, geometry.Bounds.Right + 10.0); // Place it at the end of the line
					Canvas.SetTop(adornmentText, geometry.Bounds.Top - 5.0);

					// Add the adornment to the adornment layer
					this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, TagErrors, adornmentText, null);
				}
			}
		}
	}
}
