using System;
using UnityEngine;

namespace MixedSpace
{
    /// <summary>Declares the runtime contract required by a serialized Unity object reference.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MixedSpaceComponentContractAttribute : PropertyAttribute
    {
        public MixedSpaceComponentContractAttribute(Type contractType)
        {
            if (contractType == null)
                throw new ArgumentNullException(nameof(contractType));
            if (!contractType.IsInterface)
                throw new ArgumentException("A MixedSpace component contract must be an interface.", nameof(contractType));
            ContractType = contractType;
        }

        public Type ContractType { get; }
    }
}
