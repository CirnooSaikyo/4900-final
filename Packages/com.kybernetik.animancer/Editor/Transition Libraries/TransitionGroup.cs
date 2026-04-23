// Animancer // https://kybernetik.com.au/animancer // Copyright 2018-2026 Kybernetik //

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Animancer.Editor.TransitionLibraries
{
    /// https://kybernetik.com.au/animancer/api/Animancer.Editor.TransitionLibraries/TransitionLibraryEditorDataInternal
    public partial class TransitionLibraryEditorDataInternal
    {
        /************************************************************************************************************************/

        [SerializeField]
        private List<TransitionGroup> _TransitionGroups;

        /// <summary>[<see cref="SerializeField"/>] The groups which transitions are organised in.</summary>
        public ref List<TransitionGroup> TransitionGroups
        {
            get
            {
                _TransitionGroups ??= new();
                return ref _TransitionGroups;
            }
        }

        /************************************************************************************************************************/
    }

    /// <summary>[Editor-Only]
    /// A group of transitions for display organisation in the <see cref="TransitionLibraryWindow"/>.
    /// </summary>
    /// https://kybernetik.com.au/animancer/api/Animancer.Editor.TransitionLibraries/TransitionGroup
    [Serializable]
    public class TransitionGroup :
        ICopyable<TransitionGroup>,
        IEquatable<TransitionGroup>
    {
        /************************************************************************************************************************/
        #region Fields and Properties
        /************************************************************************************************************************/

        [SerializeField]
        private string _Name;

        /// <summary>[<see cref="SerializeField"/>] The display name of this group.</summary>
        public ref string Name
            => ref _Name;

        /************************************************************************************************************************/

        [SerializeField]
        private int _Index;

        /// <summary>[<see cref="SerializeField"/>]
        /// The display index of this group within the <see cref="TransitionGroupCache.Items"/>.
        /// </summary>
        public ref int Index
            => ref _Index;

        /************************************************************************************************************************/

        [SerializeField]
        private bool _IsExpanded = true;

        /// <summary>[<see cref="SerializeField"/>] Is this group currently showing its contents?</summary>
        public ref bool IsExpanded
            => ref _IsExpanded;

        /************************************************************************************************************************/

        [SerializeField]
        private List<int> _TransitionIndices;

        /// <summary>[<see cref="SerializeField"/>]
        /// The indices of the transitions in the <see cref="Animancer.TransitionLibraries.TransitionLibraryDefinition"/>.
        /// </summary>
        public ref List<int> TransitionIndices
        {
            get
            {
                _TransitionIndices ??= new();
                return ref _TransitionIndices;
            }
        }

        /************************************************************************************************************************/

        [NonSerialized]
        private List<TransitionAssetBase> _Transitions;

        /// <summary>The transitions referenced by <see cref="TransitionIndices"/>.</summary>
        /// <remarks>This list is temporarily filled during GUI calls in the <see cref="TransitionLibraryWindow"/>.</remarks>
        public List<TransitionAssetBase> Transitions
            => _Transitions ??= new();

        /************************************************************************************************************************/
        #endregion
        /************************************************************************************************************************/
        #region Equality
        /************************************************************************************************************************/

        /// <summary>Are all fields in this object equal to the equivalent in `obj`?</summary>
        public override bool Equals(object obj)
            => Equals(obj as TransitionGroup);

        /// <summary>Are all fields in this object equal to the equivalent fields in `other`?</summary>
        public bool Equals(TransitionGroup other)
            => other != null
            && _Name == other._Name
            && _Index == other._Index
            && _IsExpanded == other._IsExpanded
            && AnimancerUtilities.ContentsAreEqual(TransitionIndices, other.TransitionIndices);

        /// <summary>Are all fields in `a` equal to the equivalent fields in `b`?</summary>
        public static bool operator ==(TransitionGroup a, TransitionGroup b)
            => a is null
            ? b is null
            : a.Equals(b);

        /// <summary>Are any fields in `a` not equal to the equivalent fields in `b`?</summary>
        public static bool operator !=(TransitionGroup a, TransitionGroup b)
            => !(a == b);

        /************************************************************************************************************************/

        /// <summary>Returns a hash code based on the values of this object's fields.</summary>
        public override int GetHashCode()
            => AnimancerUtilities.Hash(1598151553,
                _Name.GetHashCode(),
                _Index.GetHashCode(),
                _IsExpanded.GetHashCode(),
                TransitionIndices.GetHashCode());

        /************************************************************************************************************************/

        /// <inheritdoc/>
        public void CopyFrom(TransitionGroup copyFrom, CloneContext context)
        {
            _Name = copyFrom._Name;
            _Index = copyFrom._Index;
            _IsExpanded = copyFrom._IsExpanded;

            TransitionIndices.Clear();
            TransitionIndices.AddRange(copyFrom.TransitionIndices);
        }

        /************************************************************************************************************************/

        /// <inheritdoc/>
        public override string ToString()
            => $"{nameof(TransitionGroup)}({_Name}, {_Index}, {TransitionIndices.Count})";

        /************************************************************************************************************************/
        #endregion
        /************************************************************************************************************************/
    }
}

#endif

