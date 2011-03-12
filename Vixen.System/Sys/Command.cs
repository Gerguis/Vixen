﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vixen.Module.Effect;

namespace Vixen.Sys {
	// This is what represents an editable command generated by a user.
	// This has no knowledge of the command behavior, IEffect does.  This is a
	// command spec with parameter values to give it meaning.
	public class Command {
		public Command(Guid effectId, params object[] parameterValues) {
			Spec = Server.ModuleManagement.GetEffect(effectId);
			ParameterValues = parameterValues;
		}

		public object[] ParameterValues { get; private set; }
		public IEffectModuleInstance Spec { get; private set; }
	}
}
