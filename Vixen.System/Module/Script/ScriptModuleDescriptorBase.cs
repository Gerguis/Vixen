﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vixen.Module.Script {
	abstract public class ScriptModuleDescriptorBase : ModuleDescriptorBase, IScriptModuleDescriptor, IEqualityComparer<IScriptModuleDescriptor> {
		abstract public override string TypeName { get; }

		abstract public override Guid TypeId { get; }

		abstract public override Type ModuleClass { get; }

		abstract public override Type ModuleDataClass { get; }

		abstract public override string Author { get; }

		abstract public override string Description { get; }

		abstract public override string Version { get; }

		abstract public string Language { get; }

		abstract public string FileExtension { get; }

		abstract public Type SkeletonGenerator { get; }

		abstract public Type FrameworkGenerator { get; }

		abstract public Type CodeProvider { get; }

		public bool Equals(IScriptModuleDescriptor x, IScriptModuleDescriptor y) {
			return base.Equals(x, y);
		}

		public int GetHashCode(IScriptModuleDescriptor obj) {
			return base.GetHashCode();
		}
	}
}