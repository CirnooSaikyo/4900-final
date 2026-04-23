// Animancer // https://kybernetik.com.au/animancer // Copyright 2018-2026 Kybernetik //

#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEngine;

namespace Animancer.Editor.TransitionLibraries
{
    /// <summary>A list of items in the <see cref="TransitionLibraryWindow"/> organised by group.</summary>
    /// https://kybernetik.com.au/animancer/api/Animancer.Editor.TransitionLibraries/TransitionGroupCache
    public class TransitionGroupCache
    {
        /************************************************************************************************************************/

        private static readonly List<TransitionAssetBase> Transitions = new();

        private readonly List<object> Items = new();
        private readonly List<TransitionGroup> ItemToGroup = new();
        private readonly Dictionary<object, int> ItemToIndex = new();

        /************************************************************************************************************************/

        /// <summary>The total number of items in this cache.</summary>
        public int Count
            => Items.Count;

        /************************************************************************************************************************/

        /// <summary>Returns the index of the specified `item` in this cache.</summary>
        public int IndexOf(object item)
            => item != null && ItemToIndex.TryGetValue(item, out var index)
            ? index
            : -1;

        /************************************************************************************************************************/

        /// <summary>Tries to get the item at the specified index.</summary>
        public bool TryGet(int index, out object item)
            => Items.TryGet(index, out item);

        /************************************************************************************************************************/

        /// <summary>Returns the item at the specified index.</summary>
        public object GetItem(int index)
            => Items[index];

        /// <summary>Returns the group containing the item at the specified index.</summary>
        public TransitionGroup GetGroup(int index)
            => ItemToGroup[index];

        /************************************************************************************************************************/

        /// <summary>
        /// Converts the `index` to a value for the <see cref="TransitionGroup.Index"/>,
        /// meaning it skips any items inside groups.
        /// </summary>
        public int ItemToGroupIndex(int index)
        {
            index = Mathf.Clamp(index, 0, Items.Count - 1);

            for (int i = index; i >= 0; i--)
            {
                var group = ItemToGroup[i];
                if (group != null && !ReferenceEquals(group, Items[i]))
                    index--;
            }

            return index;
        }

        /************************************************************************************************************************/

        /// <summary>Gathers the items from the specified library.</summary>
        public void GatherTransitionsAndGroups(
            TransitionAssetBase[] transitions,
            TransitionLibraryEditorDataInternal editorData)
            => GatherTransitionsAndGroups(transitions, editorData.TransitionGroups);

        /// <summary>Gathers the items from the specified library.</summary>
        public void GatherTransitionsAndGroups(
            TransitionAssetBase[] transitions,
            List<TransitionGroup> groups)
        {
            Items.Clear();
            Transitions.Clear();
            ItemToGroup.Clear();
            ItemToIndex.Clear();

            Transitions.AddRange(transitions);

            SortGroups(groups);
            GatherGroupedTransitions(groups);
            GatherUnGroupedItems();

            GatherGroupedItems(groups);
            GatherItemIndices();

            Transitions.Clear();
        }

        /************************************************************************************************************************/

        /// <summary>
        /// Sorts the `groups` by <see cref="TransitionGroup.Index"/>
        /// and removes any nulls.
        /// </summary>
        public static void SortGroups(List<TransitionGroup> groups)
        {
            var previousGroupIndex = int.MinValue;
            var outOfOrder = false;

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];

                if (group == null)
                {
                    groups.RemoveAt(i);
                    i--;
                    continue;
                }

                var groupIndex = group.Index;
                if (groupIndex < previousGroupIndex)
                {
                    outOfOrder = true;
                }
                else if (groupIndex == previousGroupIndex)
                {
                    groupIndex++;
                    group.Index = groupIndex;
                }

                previousGroupIndex = groupIndex;
            }

            if (outOfOrder)
                groups.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        }

        /************************************************************************************************************************/

        /// <summary>
        /// Grabs items from the <see cref="Transitions"/>
        /// to fill the <see cref="TransitionGroup.Transitions"/>.
        /// </summary>
        private void GatherGroupedTransitions(List<TransitionGroup> groups)
        {
            for (int iGroup = 0; iGroup < groups.Count; iGroup++)
            {
                var group = groups[iGroup];
                group.Transitions.Clear();

                for (int iTransition = 0; iTransition < group.TransitionIndices.Count; iTransition++)
                {
                    var transitionIndex = group.TransitionIndices[iTransition];
                    if (!Transitions.TryGetObject(transitionIndex, out var transition))
                        continue;

                    Transitions[transitionIndex] = null;
                    group.Transitions.Add(transition);
                }
            }
        }

        /************************************************************************************************************************/

        /// <summary>Copies un-grouped transitions over to the <see cref="Items"/>.</summary>
        private void GatherUnGroupedItems()
        {
            for (int i = 0; i < Transitions.Count; i++)
            {
                var transition = Transitions[i];
                if (transition == null)
                    continue;

                Items.Add(transition);
                ItemToGroup.Add(null);
            }
        }

        /************************************************************************************************************************/

        /// <summary>Copies groups and grouped transitions over to the <see cref="Items"/>.</summary>
        private void GatherGroupedItems(List<TransitionGroup> groups)
        {
            var expandedItemOffset = 0;
            for (int iGroup = 0; iGroup < groups.Count; iGroup++)
            {
                var group = groups[iGroup];
                group.Index = Mathf.Clamp(group.Index, 0, Transitions.Count + iGroup + 1);
                var index = group.Index + expandedItemOffset;
                index = Mathf.Clamp(index, 0, Items.Count);
                Items.Insert(index, group);
                ItemToGroup.Insert(index, group);

                if (!group.IsExpanded)
                    continue;

                expandedItemOffset += group.Transitions.Count;
                for (int iTransition = 0; iTransition < group.Transitions.Count; iTransition++)
                {
                    index++;
                    Items.Insert(index, group.Transitions[iTransition]);
                    ItemToGroup.Insert(index, group);
                }
            }
        }

        /************************************************************************************************************************/

        /// <summary>Assigns the <see cref="ItemToIndex"/> for each of the <see cref="Items"/>.</summary>
        private void GatherItemIndices()
        {
            for (int i = 0; i < Items.Count; i++)
                ItemToIndex[Items[i]] = i;
        }

        /************************************************************************************************************************/
    }
}

#endif

