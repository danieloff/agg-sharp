﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.PolygonMesh;
using MatterHackers.PolygonMesh.Processors;
using MatterHackers.RayTracer;
using MatterHackers.VectorMath;

namespace MatterHackers.DataConverters3D
{
	public static class Object3DExtensions
	{
		internal static void LoadMeshLinks(this IObject3D tempScene, CancellationToken cancellationToken, Dictionary<string, IObject3D> itemCache, Action<double, string> progress)
		{
			var itemsToLoad = (from object3D in tempScene.DescendantsAndSelf()
							   where !string.IsNullOrEmpty(object3D.MeshPath)
							   select object3D).ToList();

			foreach (IObject3D object3D in itemsToLoad)
			{
				object3D.LoadLinkedMesh(itemCache, cancellationToken, progress);
			}
		}

		private static void LoadLinkedMesh(this IObject3D item, Dictionary<string, IObject3D> itemCache, CancellationToken cancellationToken, Action<double, string> progress)
		{
			string filePath = item.MeshPath;
			if (!File.Exists(filePath))
			{
				filePath = Path.Combine(Object3D.AssetsPath, filePath);
			}

			var loadedItem = Object3D.Load(filePath, cancellationToken, itemCache, progress);

			// TODO: Consider refactoring progress reporting to use an instance with state and the original delegate reference to allow anyone along the chain
			// to determine if continueProcessing has been set to false and allow for more clear aborting (rather than checking for null as we have to do below) 
			//
			// During startup we reload the main control multiple times. When the timing is right, reportProgress0to100 may set continueProcessing 
			// on the reporter to false and MeshFileIo.Load will return null. In those cases, we need to exit rather than continue processing
			if (loadedItem != null)
			{
				item.SetMeshDirect(loadedItem.Mesh);

				// TODO: When loading mesh links, if a node has children and a mesh (MeshWrapers for example) 
				// then we load the mesh and blow away the children in the assignment below. The new conditional
				// assignment accounts for that case but may need more consideration
				if (string.Equals(Path.GetExtension(filePath), ".mcx", StringComparison.OrdinalIgnoreCase))
				{
					item.Children = loadedItem.Children;
				}
			}
		}

		public static void Save(this IObject3D sourceItem, Stream stream, Action<double, string> progress = null)
		{
			sourceItem.PersistAssets(progress);

			var streamWriter = new StreamWriter(stream);
			streamWriter.Write(sourceItem.ToJson());
			streamWriter.Flush();
		}

		public static void PersistAssets(this IObject3D sourceItem, Action<double, string> progress = null)
		{
			var itemsWithUnsavedMeshes = from object3D in sourceItem.Descendants()
										 where object3D.Persistable &&
											   object3D.MeshPath == null &&
											   object3D.Mesh != null
										 select object3D;

			string assetsDirectory = Object3D.AssetsPath;
			Directory.CreateDirectory(assetsDirectory);

			var assetFiles = new Dictionary<int, string>();

			try
			{
				// Write each unsaved mesh to disk
				foreach (IObject3D item in itemsWithUnsavedMeshes)
				{
					// Calculate the mesh hash
					int hashCode = (int)item.Mesh.GetLongHashCode();

					string assetPath;

					bool savedSuccessfully = true;

					if (!assetFiles.TryGetValue(hashCode, out assetPath))
					{
						// Get an open filename
						string tempStlPath = CreateNewLibraryPath(".stl");

						// Save the embedded asset to disk
						savedSuccessfully = MeshFileIo.Save(
							new Object3D() { Mesh = item.Mesh },
							tempStlPath,
							CancellationToken.None,
							new MeshOutputSettings(MeshOutputSettings.OutputType.Binary),
							progress);

						if (savedSuccessfully)
						{
							// There's currently no way to know the actual mesh file hashcode without saving it to disk, thus we save at least once in
							// order to compute the hash but then throw away the duplicate file if an existing copy exists in the assets directory
							string sha1 = MeshFileIo.ComputeSHA1(tempStlPath);
							assetPath = Path.Combine(assetsDirectory, sha1 + ".stl");
							if (!File.Exists(assetPath))
							{
								File.Copy(tempStlPath, assetPath);
							}

							// Remove the temp file
							File.Delete(tempStlPath);

							assetFiles.Add(hashCode, assetPath);
						}
					}

					if (savedSuccessfully && File.Exists(assetPath))
					{
						// Assets should be stored relative to the Asset folder
						item.MeshPath = Path.GetFileName(assetPath);
					}
				}
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Error saving file: ", ex.Message);
			}
		}

		/// <summary>
		/// Creates a new non-colliding library file path to write library contents to
		/// </summary>
		/// <param name="extension">The file extension to use</param>
		/// <returns>A new unique library path</returns>
		private static string CreateNewLibraryPath(string extension)
		{
			string filePath;
			do
			{
				filePath = Path.Combine(Object3D.AssetsPath, Path.ChangeExtension(Path.GetRandomFileName(), extension));
			} while (File.Exists(filePath));

			return filePath;
		}

		public static AxisAlignedBoundingBox GetUnionedAxisAlignedBoundingBox(this IEnumerable<IObject3D> items)
		{
			// first find the bounds of what is already here.
			AxisAlignedBoundingBox totalBounds = AxisAlignedBoundingBox.Empty;
			foreach (var object3D in items)
			{
				totalBounds = AxisAlignedBoundingBox.Union(totalBounds, object3D.GetAxisAlignedBoundingBox(Matrix4X4.Identity));
			}

			return totalBounds;
		}

		public static Matrix4X4 WorldMatrix(this IObject3D child, IObject3D rootOverride = null)
		{
			var matrix = child.Matrix;
			var parent = child.Parent;

			while (parent != null)
			{
				matrix = matrix * parent.Matrix;

				if (parent == rootOverride)
				{
					break;
				}

				parent = parent.Parent;
			}

			return matrix;
		}

		public static List<IObject3D> Ancestors(this IObject3D child, bool includeThis = true)
		{
			List<IObject3D> ancestors = new List<IObject3D>();
			if (includeThis)
			{
				ancestors.Add(child);
			}
			var parent = child.Parent;
			while (parent != null)
			{
				ancestors.Add(parent);
				parent = parent.Parent;
			}

			return ancestors;
		}

		public static void Unwrap(this IObject3D item)
		{
			foreach (var child in item.Children)
			{
				child.Matrix *= item.Matrix;
			}

			item.Parent.Children.Modify(list =>
			{
				list.Remove(item);
				list.AddRange(item.Children);
			});
		}

		public static Color WorldColor(this IObject3D child, IObject3D rootOverride = null)
		{
			var lastColorFound = Color.White;
			foreach(var item in child.Ancestors())
			{
				// if we find a color it overrides our current color so set it
				if (item.Color.Alpha0To255 != 0)
				{
					lastColorFound = item.Color;
				}

				// If the root override has been matched, break and return latest
				if (item == rootOverride)
				{
					break;
				}
			}

			return lastColorFound;
		}

		public static PrintOutputTypes WorldOutputType(this IObject3D child, IObject3D rootOverride = null)
		{
			var lastOutputTypeFound = PrintOutputTypes.Default;
			foreach (var item in child.Ancestors())
			{
				if (item.OutputType != PrintOutputTypes.Default)
				{
					// use collection as the color for all recursive children
					lastOutputTypeFound = item.OutputType;
				}

				// If the root override has been matched, break and return latest
				if (item == rootOverride)
				{
					break;
				}
			}

			return lastOutputTypeFound;
		}

		public static int WorldMaterialIndex(this IObject3D child, IObject3D rootOverride = null)
		{
			var lastMaterialIndexFound = -1;
			foreach (var item in child.Ancestors())
			{
				if (item.MaterialIndex != -1)
				{
					// use collection as the color for all recursive children
					lastMaterialIndexFound = item.MaterialIndex;
				}

				// If the root override has been matched, break and return latest
				if (item == rootOverride)
				{
					break;
				}
			}

			// If we don't find a color (-1) return 0
			return lastMaterialIndexFound;
		}

		public static IEnumerable<IObject3D> DescendantsAndSelf(this IObject3D root)
		{
			var items = new Stack<IObject3D>(new[] { root });
			while (items.Any())
			{
				IObject3D item = items.Pop();

				yield return item;
				foreach (var n in item.Children)
				{
					// This is code that ensures the tree is bulid with parent pointers correctly
					n.Parent = item;
					items.Push(n);
				}
			}
		}

		public static IEnumerable<IObject3D> Descendants(this IObject3D root)
		{
			var items = new Stack<IObject3D>();

			foreach (var n in root.Children)
			{
				// This is code that ensures the tree is bulid with parent pointers correctly
				n.Parent = root;
				items.Push(n);
			}

			while (items.Any())
			{
				IObject3D item = items.Pop();

				yield return item;
				foreach (var n in item.Children)
				{
					// This is code that ensures the tree is bulid with parent pointers correctly
					n.Parent = item;
					items.Push(n);
				}
			}
		}

		public static void Rotate(this IObject3D item, Vector3 origin, Vector3 axis, double angle)
		{
			// move object relative to rotation
			item.Matrix *= Matrix4X4.CreateTranslation(-origin);
			// rotate it
			item.Matrix *= Matrix4X4.CreateRotation(axis, angle);
			// move it back
			item.Matrix *= Matrix4X4.CreateTranslation(origin);
		}

		public static IPrimitive CreateTraceData(this Mesh mesh, int maxRecursion = int.MaxValue)
		{
			List<IPrimitive> allPolys = new List<IPrimitive>();
			List<Vector3> positions = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();

			foreach (Face face in mesh.Faces)
			{
				positions.Clear();
				bool hasTexture = false;

				foreach (FaceEdge faceEdge in face.FaceEdges())
				{
					if (mesh.TextureUV.ContainsKey((faceEdge, 0)))
					{
						uvs.Add(faceEdge.GetUv(0));
						hasTexture = true;
					}
					positions.Add(faceEdge.FirstVertex.Position);
				}

				// We should use the tessellator for this if it is greater than 3.
				Vector3 next = positions[1];
				Vector2 nextuv = hasTexture ? uvs[1] : Vector2.Zero;
				for (int positionIndex = 2; positionIndex < positions.Count; positionIndex++)
				{
					TriangleShape triangel;
					if (hasTexture)
					{
						triangel = new TriangleShapeUv(positions[0], next, positions[positionIndex],
							uvs[0], nextuv, uvs[positionIndex], null);
					}
					else
					{
						triangel = new TriangleShape(positions[0], next, positions[positionIndex], null);
					}
					allPolys.Add(triangel);
					next = positions[positionIndex];
				}
			}

			return BoundingVolumeHierarchy.CreateNewHierachy(allPolys, maxRecursion);
		}

		/* 
		public class SelectionChangeCommand : IUndoRedoCommand
		{
			public Color Color{ get; set; }
			public int MaterialIndex { get; set; }
			PrintOutputTypes PrintOutputTypes { get; set; }
			Matrix4X4 Matrix { get; set; }

			public void Do()
			{
				
			}

			public void Undo()
			{
				throw new NotImplementedException();
			}
		}*/

		/// <summary>
		/// Collapses the source object into the target list (often but not necessarily the scene)
		/// </summary>
		/// <param name="objectToCollapse">The object to collapse</param>
		/// <param name="collapseInto">The target to collapse into</param>
		/// <param name="typeFilter">Type filter</param>
		/// <param name="depth">?</param>
		public static void CollapseInto(this IObject3D objectToCollapse, List<IObject3D> collapseInto, bool filterToSelectionGroup = true, int depth = int.MaxValue)
		{
			if (objectToCollapse != null 
				&& objectToCollapse is SelectionGroup == filterToSelectionGroup)
			{
				// Remove the collapsing item from the list
				collapseInto.Remove(objectToCollapse);

				// Move each child from objectToCollapse into the target (often the scene), applying the parent transform to each
				foreach (var child in objectToCollapse.Children)
				{
					if (objectToCollapse.Color != Color.Transparent)
					{
						child.Color = objectToCollapse.Color;
					}

					if (objectToCollapse.MaterialIndex != -1)
					{
						child.MaterialIndex = objectToCollapse.MaterialIndex;
					}

					if (objectToCollapse.OutputType != PrintOutputTypes.Default)
					{
						child.OutputType = objectToCollapse.OutputType;
					}

					child.Matrix *= objectToCollapse.Matrix;

					if (child is SelectionGroup && depth > 0)
					{
						child.CollapseInto(collapseInto, filterToSelectionGroup, depth - 1);
					}
					else
					{
						collapseInto.Add(child);
					}
				}
			}
		}
	}
}