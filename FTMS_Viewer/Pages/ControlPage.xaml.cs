namespace FTMS_Viewer.Pages;

using Microsoft.Maui.Controls;

public partial class ControlPage : ContentPage
{
	public ControlPage(ControlViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}

	/// <summary>
	/// Marks a target-setting field dirty only while it is focused, so prefill from machine state
	/// (which changes the text of an unfocused field) does not count as a user edit.
	/// </summary>
	private void OnTargetValueTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is BindableObject bindable
			&& bindable is Entry { IsFocused: true }
			&& bindable.BindingContext is TargetValueItem item)
		{
			item.MarkDirty();
		}
	}
}

/// <summary>
/// Picks the value-card template for <see cref="TargetValueItem"/>s and the plain button template
/// for control operations.
/// </summary>
public sealed class ControlItemTemplateSelector : DataTemplateSelector
{
	public DataTemplate? OperationTemplate { get; set; }

	public DataTemplate? ValueTemplate { get; set; }

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
		=> item is TargetValueItem ? this.ValueTemplate! : this.OperationTemplate!;
}
