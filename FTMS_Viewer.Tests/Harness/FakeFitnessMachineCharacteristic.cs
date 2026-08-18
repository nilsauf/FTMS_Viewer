namespace FTMS_Viewer.Tests.Harness;

using FTMS.NET;
using System.Reactive.Subjects;

/// <summary>
/// A fake <see cref="IFitnessMachineCharacteristic"/>: raw byte frames can be pushed into its
/// notification stream, and every byte written through <see cref="WriteValueAsync(byte[])"/>
/// is captured for assertion.
/// </summary>
public sealed class FakeFitnessMachineCharacteristic : IFitnessMachineCharacteristic
{
	private readonly Subject<byte[]> valueFeed = new();
	private readonly List<byte[]> writtenValues = [];

	public FakeFitnessMachineCharacteristic(Guid id)
	{
		this.Id = id;
	}

	public Guid Id { get; }

	/// <summary>The exact bytes written to this characteristic, in order.</summary>
	public IReadOnlyList<byte[]> WrittenValues => this.writtenValues;

	/// <summary>The value returned by <see cref="ReadValueAsync()"/>.</summary>
	public byte[]? ReadValue { get; set; }

	/// <summary>When set, <see cref="WriteValueAsync(byte[])"/> throws this exception.</summary>
	public Exception? WriteException { get; set; }

	public Task<byte[]> ReadValueAsync()
		=> Task.FromResult(this.ReadValue ?? []);

	public Task WriteValueAsync(byte[] value)
	{
		if (this.WriteException is { } exception)
			throw exception;

		this.writtenValues.Add([.. value]);
		return Task.CompletedTask;
	}

	public IObservable<byte[]> ObserveValue()
		=> this.valueFeed;

	/// <summary>Pushes a raw frame into the characteristic's notification stream.</summary>
	public void Feed(byte[] frame)
		=> this.valueFeed.OnNext(frame);

	/// <summary>Completes the characteristic's notification stream.</summary>
	public void Complete()
		=> this.valueFeed.OnCompleted();
}
