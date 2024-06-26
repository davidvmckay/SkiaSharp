﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SkiaSharp.Tests
{
	public class SKObjectTest : SKTest
	{
		[SkippableFact]
		public void CanInstantiateAbstractClassesWithImplementation()
		{
			var handle = GetNextPtr();

			var obj = SKObject.GetOrAddObject<AbstractObject>(handle, (h, o) => new ConcreteObject(h, o));

			Assert.NotNull(obj);
			Assert.IsType<ConcreteObject>(obj);
		}

		private abstract class AbstractObject : SKObject
		{
			public AbstractObject(IntPtr handle, bool owns)
				: base(handle, owns)
			{
			}
		}

		private class ConcreteObject : AbstractObject
		{
			public ConcreteObject(IntPtr handle, bool owns)
				: base(handle, owns)
			{
			}
		}

		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.Android)] // Mono does not guarantee finalizers are invoked immediately
		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.iOS)] // Mono does not guarantee finalizers are invoked immediately
		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.MacCatalyst)] // Mono does not guarantee finalizers are invoked immediately
		[SkippableFact]
		public void SameHandleReturnsSameReferenceAndReleasesObject()
		{
			var handle = GetNextPtr();
			TestConstruction(handle);

			CollectGarbage();

			// there should be nothing if the GC ran
			Assert.False(SKObject.GetInstance<LifecycleObject>(handle, out var inst));
			Assert.Null(inst);

			static void TestConstruction(IntPtr h)
			{
				// make sure there is nothing
				Assert.False(SKObject.GetInstance(h, out LifecycleObject i));
				Assert.Null(i);

				// get/create the object
				var first = LifecycleObject.GetObject(h);

				// get the same one
				Assert.True(SKObject.GetInstance(h, out i));
				Assert.NotNull(i);

				// compare
				Assert.Same(first, i);

				// get/create the object
				var second = LifecycleObject.GetObject(h);

				// compare
				Assert.Same(first, second);
			}
		}

		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.Android)] // Mono does not guarantee finalizers are invoked immediately
		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.iOS)] // Mono does not guarantee finalizers are invoked immediately
		[Trait(Traits.SkipOn.Key, Traits.SkipOn.Values.MacCatalyst)] // Mono does not guarantee finalizers are invoked immediately
		[SkippableFact]
		public void ObjectsWithTheSameHandleButDoNotOwnTheirHandlesAreCreatedAndCollectedCorrectly()
		{
			var handle = GetNextPtr();

			Construct(handle);

			CollectGarbage();

			// they should be gone
			Assert.False(SKObject.GetInstance<LifecycleObject>(handle, out _));

			static void Construct(IntPtr handle)
			{
				// create two objects with the same handle
				var inst1 = new LifecycleObject(handle, false);
				var inst2 = new LifecycleObject(handle, false);

				// they should never be the same
				Assert.NotSame(inst1, inst2);
			}
		}

		[SkippableFact]
		public void ObjectsWithTheSameHandleButDoNotOwnTheirHandlesAreCreatedAndDisposedCorrectly()
		{
			var handle = GetNextPtr();

			var inst = Construct(handle);

			CollectGarbage();

			// the second object is still alive
			Assert.True(SKObject.GetInstance<LifecycleObject>(handle, out var obj));
			Assert.Equal(2, obj.Value);
			Assert.Same(inst, obj);

			static LifecycleObject Construct(IntPtr handle)
			{
				// create two objects
				var inst1 = new LifecycleObject(handle, false) { Value = 1 };
				var inst2 = new LifecycleObject(handle, false) { Value = 2 };

				// make sure thy are different and the first is disposed
				Assert.NotSame(inst1, inst2);
				Assert.True(inst1.DestroyedManaged);

				// because the object does not own the handle, the native is untouched
				Assert.False(inst1.DestroyedNative);

				return inst2;
			}
		}

		[SkippableFact]
		public void ObjectsWithTheSameHandleAndOwnTheirHandlesThrowInDebugBuildsButNotRelease()
		{
			var handle = GetNextPtr();

			var inst1 = new LifecycleObject(handle, true) { Value = 1 };

#if THROW_OBJECT_EXCEPTIONS
			var ex = Assert.Throws<InvalidOperationException>(() => new LifecycleObject(handle, true) { Value = 2 });
			Assert.Contains($"H: {handle.ToString("x")} ", ex.Message);
#else
			var inst2 = new LifecycleObject(handle, true) { Value = 2 };
			Assert.True(inst1.DestroyedNative);

			inst1.Dispose();
			inst2.Dispose();
#endif
		}

		[SkippableFact]
		public void DisposeInvalidatesObject()
		{
			var handle = GetNextPtr();

			var obj = LifecycleObject.GetObject(handle);

			Assert.Equal(handle, obj.Handle);
			Assert.False(obj.DestroyedNative);

			obj.Dispose();

			Assert.Equal(IntPtr.Zero, obj.Handle);
			Assert.True(obj.DestroyedNative);
		}

		[SkippableFact]
		public void DisposeDoesNotInvalidateObjectIfItIsNotOwned()
		{
			var handle = GetNextPtr();

			var obj = LifecycleObject.GetObject(handle, false);

			Assert.False(obj.DestroyedNative);

			obj.Dispose();

			Assert.False(obj.DestroyedNative);
		}

		[SkippableFact]
		public void ExceptionsThrownInTheConstructorFailGracefully()
		{
			BrokenObject broken = null;
			try
			{
				broken = new BrokenObject();
			}
			catch (Exception)
			{
			}
			finally
			{
				broken?.Dispose();
				broken = null;
			}

			// trigger the finalizer
			CollectGarbage();
		}

		private class LifecycleObject : SKObject
		{
			public bool DestroyedNative = false;
			public bool DestroyedManaged = false;

			public LifecycleObject(IntPtr handle, bool owns)
				: base(handle, owns)
			{
			}

			public object Value { get; set; }

			protected override void DisposeNative()
			{
				DestroyedNative = true;
			}

			protected override void DisposeManaged()
			{
				DestroyedManaged = true;
			}

			public static LifecycleObject GetObject(IntPtr handle, bool owns = true) =>
				GetOrAddObject(handle, owns, (h, o) => new LifecycleObject(h, o));
		}

		private class BrokenObject : SKObject
		{
			public BrokenObject()
				: base(broken_native_method(), true)
			{
			}

			private static IntPtr broken_native_method()
			{
				throw new Exception("BREAK!");
			}
		}

		[SkippableTheory]
		[InlineData(1)]
		[InlineData(1000)]
		public async Task EnsureMultithreadingDoesNotThrow(int iterations)
		{
			var imagePath = Path.Combine(PathToImages, "baboon.jpg");

			var tasks = new Task[iterations];

			for (var i = 0; i < iterations; i++)
			{
				var task = new Task(() =>
				{
					using (var stream = File.OpenRead(imagePath))
					using (var data = SKData.Create(stream))
					using (var codec = SKCodec.Create(data))
					{
						var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);
						using (var image = SKBitmap.Decode(codec, info))
						{
							var img = new byte[image.Height, image.Width];
						}
					}
				});

				tasks[i] = task;
				task.Start();
			}

			await Task.WhenAll(tasks);
		}

		[SkippableFact]
		public void EnsureConcurrencyResultsInCorrectDeregistration()
		{
			var handle = GetNextPtr();

			var obj = new ImmediateRecreationObject(handle, true);
			Assert.Null(obj.NewInstance);
			Assert.Equal(obj, HandleDictionary.instances[handle]?.Target);

			obj.Dispose();
			Assert.True(SKObject.GetInstance<ImmediateRecreationObject>(handle, out _));

			var newObj = obj.NewInstance;

			var weakReference = HandleDictionary.instances[handle];
			Assert.True(weakReference.IsAlive);
			Assert.NotEqual(obj, weakReference.Target);
			Assert.Equal(newObj, weakReference.Target);

			newObj.Dispose();
			Assert.False(SKObject.GetInstance<ImmediateRecreationObject>(handle, out _));
		}

		private class ImmediateRecreationObject : SKObject
		{
			public ImmediateRecreationObject(IntPtr handle, bool shouldRecreate)
				: base(handle, true)
			{
				ShouldRecreate = shouldRecreate;
			}

			public bool ShouldRecreate { get; }

			public ImmediateRecreationObject NewInstance { get; private set; }

			protected override void DisposeNative()
			{
				base.DisposeNative();

				if (ShouldRecreate)
					NewInstance = new ImmediateRecreationObject(Handle, false);
			}
		}

		[SkippableFact]
		public void ManagedObjectsAreDisposedBeforeNative()
		{
			var parent1 = ParentChildWorld.GetParent("Root");
			var child1 = parent1.Child;

			parent1.Dispose();

			var unownedParent = ParentChildWorld.DisposeUnownedManagedParent;
			var unownedChild =  ParentChildWorld.DisposeUnownedManagedChild;

			var managedParent = ParentChildWorld.DisposeManagedParent;
			var managedChild = ParentChildWorld.DisposeManagedChild;

			var nativeParent = ParentChildWorld.DisposeNativeParent;
			var nativeChild = ParentChildWorld.DisposeNativeChild;

			Assert.True(child1.IsDisposed);
			Assert.True(parent1.IsDisposed);

			Assert.Equal("DisposeUnownedManaged", unownedParent.State);
			Assert.Equal("DisposeUnownedManaged", unownedChild.State);

			Assert.Equal("DisposeUnownedManaged", managedParent.State);
			Assert.Equal("DisposeUnownedManaged", managedChild.State);

			Assert.Equal("DisposeUnownedManaged", nativeParent.State);
			Assert.Equal("DisposeUnownedManaged", nativeChild.State);

			unownedParent.Dispose();
			managedParent.Dispose();
			nativeParent.Dispose();
		}

		private static class ParentChildWorld
		{
			public static readonly IntPtr ParentHandle = GetNextPtr();
			public static readonly IntPtr ChildHandle = GetNextPtr();

			public static ParentObject GetParent(string state) =>
				SKObject.GetOrAddObject(ParentHandle, (h, o) => new ParentObject(state, h, o));

			public static ParentObject DisposeManagedParent;
			public static ChildObject DisposeManagedChild;

			public static ParentObject DisposeNativeParent;
			public static ChildObject DisposeNativeChild;

			public static ParentObject DisposeUnownedManagedParent;
			public static ChildObject DisposeUnownedManagedChild;
		}

		private class ParentObject : SKObject
		{
			public ParentObject(string state, IntPtr handle, bool owns)
				: base(handle, owns)
			{
				State = state;
			}

			public ChildObject Child =>
				OwnedBy(GetOrAddObject(ParentChildWorld.ChildHandle, false, (h, o) => new ChildObject(State, h, o)), this);

			public string State { get; }

			protected override void DisposeUnownedManaged()
			{
				base.DisposeUnownedManaged();

				if (State == "Root")
				{
					ParentChildWorld.DisposeUnownedManagedParent = ParentChildWorld.GetParent("DisposeUnownedManaged");
					ParentChildWorld.DisposeUnownedManagedChild = ParentChildWorld.DisposeUnownedManagedParent.Child;
				}
			}

			protected override void DisposeManaged()
			{
				base.DisposeManaged();

				if (State == "Root")
				{
					ParentChildWorld.DisposeManagedParent = ParentChildWorld.GetParent("DisposeManaged");
					ParentChildWorld.DisposeManagedChild = ParentChildWorld.DisposeManagedParent.Child;
				}
			}

			protected override void DisposeNative()
			{
				base.DisposeNative();

				if (State == "Root")
				{
					ParentChildWorld.DisposeNativeParent = ParentChildWorld.GetParent("DisposeNative");
					ParentChildWorld.DisposeNativeChild = ParentChildWorld.DisposeNativeParent.Child;
				}
			}

			public override string ToString() =>
				$"Parent: {State}";
		}

		private class ChildObject : SKObject
		{
			public ChildObject(string state, IntPtr handle, bool owns)
				: base(handle, owns)
			{
				State = state;
			}

			public string State { get; }

			public override string ToString() =>
				$"Child: {State}";
		}
	}
}
