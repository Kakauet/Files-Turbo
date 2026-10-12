using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml.Controls;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class ImagePreview : UserControl
	{
		public ImagePreview(ImagePreviewViewModel model)
		{
			ViewModel = model;
			InitializeComponent();
			Loaded += (_, _) => UpdatePreviewSize();
			SizeChanged += (_, _) => UpdatePreviewSize();
			Unloaded += ViewModel.PreviewControlBase_Unloaded;
		}

		private ImagePreviewViewModel ViewModel { get; set; }

		// A collapsed pane reports no size; the view model then decodes at its default bound
		private void UpdatePreviewSize()
		{
			if (XamlRoot is { } root)
				ViewModel.SetPreviewSize(ActualWidth, ActualHeight, root.RasterizationScale);
		}
	}
}
