// Marmoset Skyshop
// Copyright 2013 Marmoset LLC
// http://marmoset.co

using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.IO;
using System.Linq;

namespace mset {
	[Serializable]
	public class CubemapGUI : ScriptableObject {
		//public HideFlags hideFlags = 0;

		public static CubemapGUI create(Type _type, bool _inspector) {
			CubemapGUI cg = ScriptableObject.CreateInstance<mset.CubemapGUI>();
			//CubemapGUI cg = new CubemapGUI();
			cg.setType(_type, _inspector);
			cg.OnEnable();
			return cg;
		}

		public enum Type {
			NIL,
			INPUT,
			SKY,
			SIM,
			DIM
		};
		
		public enum Layout {
			PANO,
			CROSS,
			ROW
		};
		
		// cube buffer is huge, don't serialize it, recompute it on load
		[NonSerialized]
		public CubeBuffer[]	buffers = { new CubeBuffer(), new CubeBuffer(), new CubeBuffer(), new CubeBuffer() };

		[NonSerialized]
		public Texture input = null;

		[NonSerialized]
		public Cubemap cube = null;

		[NonSerialized]
		public RenderTexture RT = null;

		public mset.SHEncoding SH = null;

		public bool computeSH = false;
		
		private SerializedObject srInput = null;
		
		[NonSerialized]
		public Texture2D preview = null;
		
		
		private static Texture2D lightGizmo;
		private static Texture2D pickerIcon;

		public string		dir = "";
		public string		skyName = "";
		public string		prettyPath = "";
		public string		fullPath = "";
		public string 		inputPath = "";
		public bool			mipmapped = false;
		public Type			type = Type.DIM;

		private string		queuedPath = "";
			
		public bool			inspector = false;		//some features are disabled in inspector mode
		public bool			allowTexture = false;	//allow any kind of texture as input
		public bool			allowRenderTexture = true;
		
		public bool			showLights = false;
		public bool			assetLinear = false;
		public bool			renderLinear = false;
		public bool			fullPreview = false;
		public int			previewWidth = 320;

		[NonSerialized]
		public mset.Sky		sky = null;

		[NonSerialized]
		public bool 		locked = false;
		
		//reference to input panorama GUI
		[NonSerialized]  	
		public CubemapGUI	inputGUI = null;
		
		public Layout		previewLayout = Layout.ROW;
		private Texture2D	tempTexture = null;
		
		public static CubeBuffer.FilterMode sFilterMode = CubeBuffer.FilterMode.BILINEAR;
		
		//read-only rectangle 
		private Rect previewRect = new Rect(0,0,0,0);
		
		public ColorMode	colorMode = ColorMode.RGBM8;
		
		//read-only bool for the HDR checkbox
		public bool HDR {
			get { return colorMode == ColorMode.RGBM8; }
			set { colorMode = value ? ColorMode.RGBM8 : ColorMode.RGB8; }
		}
		
		public void setType(Type _type, bool _inspector) {
			type = _type;
			inspector = _inspector;
			mipmapped = type == Type.SIM;
			allowTexture = type == Type.INPUT;
			allowRenderTexture = true;
		}
		
		public CubemapGUI() {
			//hideFlags |= HideFlags.HideAndDontSave;
			//
		}

		public void OnEnable() {
			clear();
			hideFlags |= HideFlags.HideAndDontSave;

			if (lightGizmo == null) {
				lightGizmo = Resources.Load("light") as Texture2D;
			}

			if (pickerIcon == null) {
				if (EditorGUIUtility.isProSkin) {
					pickerIcon = Resources.Load("white_picker") as Texture2D;
				}
				else {
					pickerIcon = Resources.Load("picker") as Texture2D;
				}
			}
			//NOTE: buffers are now allocated along with The CubemapGUI, what's internal to the buffers is still controlled strictly
			/*if (buffers == null) {
				buffers = new CubeBuffer[4];
				for (int i = 0; i < buffers.Length; ++i) {
					buffers[i] = new CubeBuffer();
				}
			}*/
			if(SH == null) SH = new mset.SHEncoding();

			setType(type, inspector);

			//updates path, buffers, and preview
			reloadReference();
		}
		
		public void freeMem() {
			if(locked) return;
			UnityEngine.Object.DestroyImmediate(preview,false);
			UnityEngine.Object.DestroyImmediate(tempTexture,false);
			preview = null;
			tempTexture = null;
			cube = null;			
			RT = null;
			input = null;
			srInput = null;
			SH = null;
			foreach(CubeBuffer buffer in buffers) {
				buffer.clear();
			}
		}
		
		// REFERENCE
		public void clear() {
			if(locked) return;
			cube = null;
			RT = null;
			input = null;
			srInput = null;
			dir = "";
			skyName = "";
			fullPath = "";
			prettyPath = "";
			assetLinear = false;
			if(SH != null) SH.clearToBlack();
			foreach(CubeBuffer buffer in buffers) {
				buffer.clear();
			}
		}

		public void find() {
			//look for existing cubemaps with list of known postfixes
			string foundPath = findInput();
			if (foundPath.Length > 0) {
				//TODO: technically we should decide if the found cube is mipped or not right here
				setReference(foundPath, mipmapped, true);
				EditorGUIUtility.PingObject(input);
			}
		}

		public void newCube() {
			int newSize = 64;
			switch (type) {
				case Type.INPUT:
					newSize = 512;
					break;
				case Type.SKY:
					newSize = 512;
					break;
				case Type.DIM:
					newSize = 64;
					break;
				case Type.SIM:
					newSize = 256;
					break;
			};	
			newCube(newSize);
		}

		public void newCube(int newSize) {
			string path = inputPath;

			// create untitled cubes to the same place lightmaps end up in
			if(this.inspector || path.Length == 0) {
				#if UNITY_5_3_OR_NEWER
					UnityEngine.SceneManagement.Scene currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
					if(string.IsNullOrEmpty(currentScene.path)) {
						bool saved = EditorUtility.DisplayDialog("Scene needs saving", "You need to save the scene before creating new probe cubemaps.", "Save Scene", "Cancel");
						if(saved) {
							saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene);
						}
						if(!saved) return;
					}
					string scenePath = currentScene.path;
				#else
					if(EditorApplication.currentScene == null || EditorApplication.currentScene.Length == 0) {
						bool saved = EditorUtility.DisplayDialog("Scene needs saving", "You need to save the scene before creating new probe cubemaps.", "Save Scene", "Cancel");
						if(saved) {
							saved = EditorApplication.SaveScene();
						}
						if(!saved) return;
					}
					string scenePath = EditorApplication.currentScene;
				#endif

				if(path.Length == 0) {
					string sceneDir = System.IO.Path.GetDirectoryName(scenePath);
					string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

					path = sceneDir + "/" + sceneName;
					if( !System.IO.Directory.Exists(path) ) {
						System.IO.Directory.CreateDirectory(path);
					}
					string refName = skyName;
					if( refName.Length == 0 ) refName = "untitled";
					path +=  "/" + refName;
				}
			}

			fullPath = suggestPath(path, true);
			updatePath();
			cube = new Cubemap(newSize, TextureFormat.ARGB32, mipmapped);
			cube.filterMode = FilterMode.Trilinear;
			cube.name = skyName;
			mset.Util.clearCheckerCube(ref cube);
			input = cube;
			mset.AssetUtil.setLinear(new SerializedObject(input), false);
			AssetDatabase.CreateAsset(cube, fullPath);
			setReference(fullPath, mipmapped, true);
		}

		public bool isLinear() {
			if(srInput == null) return assetLinear;
			srInput.UpdateIfRequiredOrScript();
			return mset.AssetUtil.isLinear(srInput);
		}
		
		public void setLinear( bool yes ) {
			if(srInput != null) mset.AssetUtil.setLinear(srInput, yes);
			//NOTE: assetLinear is updated in updateProperties later, which also knows how to handle preview and buffer refreshes
		}



		public void reloadReference() {
			if(this.fullPath.Length > 0) {
				setReference(this.fullPath, this.mipmapped, true);
			}
		}

		public void setReference(Texture tex, bool mipped) { setReference(tex, mipped, false); }
		public void setReference(Texture tex, bool mipped, bool fix) {
			if(locked) return;

			if( tex && tex.GetType() == typeof(Cubemap) ) {
				input = tex;
			} else if( tex && tex.GetType() == typeof(Texture2D) && allowTexture ) {
				input = tex;
			} else if( tex && tex.GetType() == typeof(RenderTexture) && allowRenderTexture ) {
				input = tex;
			} else {
				clear();
				return;
			}

			RT = input as RenderTexture;
			cube = input as Cubemap;

			fullPath = "";
			mipmapped = mipped;
			updatePath();
			updateInput(fix);
		}

		public void setReference(string path, bool mipped) { setReference(path, mipped, false); }
		public void setReference(string path, bool mipped, bool fix) {
			if(locked) return;
			if( allowTexture ) {
				input = AssetDatabase.LoadAssetAtPath( path, typeof(Texture) ) as Texture;
			} else {
				cube = AssetDatabase.LoadAssetAtPath( path, typeof(Cubemap) ) as Cubemap;
				cube.filterMode = FilterMode.Trilinear;
				input = cube;
			}
			if( input == null ) {
				clear();
				return;
			}
			RT = null;

			fullPath = path;
			mipmapped = mipped;
			updatePath();
			updateInput(fix);
		}

		private void fixInput() {
			if( srInput == null ) return;
			if( inspector ) return; // this is not an inspector check

			mset.AssetUtil.setReadable(srInput, true);

			bool is2D = input.GetType() == typeof(Texture2D);
			bool isRT = input.GetType() == typeof(RenderTexture);
			bool npotScale = false;
			bool linear = false;
			bool compressed = false;
			bool mipped = false;
			bool tooSmall = false;

			string ipath = AssetDatabase.GetAssetPath(input);
			if( is2D ) {
				//limits on Texture2D
				TextureImporter ti = mset.AssetUtil.getTextureImporter(ipath, "CubemapGUI.fixInput");
				npotScale = ti.npotScale != TextureImporterNPOTScale.None;
				tooSmall = ti.maxTextureSize < 4096;// Mathf.Max(input.width,input.height);
				linear = !ti.sRGBTexture;
				compressed = ti.textureCompression != TextureImporterCompression.Uncompressed;
				mipped = ti.mipmapEnabled;
			} else if( isRT ) {
				npotScale = false;
				tooSmall = false;
				linear = !RT.sRGB;
				compressed = false;
				mipped = RT.useMipMap;
			} else {
				//limits on cubemaps
				npotScale = false;
				tooSmall = false;
				linear = mset.AssetUtil.isLinear(srInput);
				compressed = !mset.AssetUtil.isUncompressed(srInput);
				mipped = mset.AssetUtil.isMipmapped(srInput);
			}

			//verification step: inputs ought to be sRGB sampled, uncompressed, and in case of input images, big as can be
			if ( linear || compressed || (mipped != this.mipmapped) || tooSmall || npotScale ) {
				string problems = "";
				if( linear ) problems +=					"\n- Should not be linear (don't bypass sRGB sampling)";
				if( compressed ) problems +=				"\n- Should not be compressed";
				if( mipped != this.mipmapped )  problems += "\n- " + (this.mipmapped ? "Requires " : "Does not need ") + "mipmaps";
				if( tooSmall ) problems += 					"\n- Is limited by max texture size";
				if( npotScale ) problems += 				"\n- Is limited by non-power-of-2 scaling";
				if( EditorUtility.DisplayDialog("Fix Texture Format?", "The chosen texture has a few formatting problems:" + problems, "Fix Now", "Ignore") ) {
					if( is2D ) {
						TextureImporter ti = mset.AssetUtil.getTextureImporter(ipath, "CubemapGUI.fixInput");
						ti.sRGBTexture = true;
						ti.textureCompression = TextureImporterCompression.Uncompressed;
						if(  mipped != this.mipmapped ) ti.mipmapEnabled = this.mipmapped;
						if( tooSmall ) ti.maxTextureSize = 4096;
						ti.npotScale = TextureImporterNPOTScale.None;
						AssetDatabase.ImportAsset(ipath);
					} else if( isRT ) {
						RT.useMipMap = this.mipmapped;
						//TODO: generate mips on/off?
					} else {
						mset.AssetUtil.setLinear(srInput, false);
						mset.AssetUtil.setUncompressed(srInput);
						mset.AssetUtil.setMipmapped(srInput, this.mipmapped);
					}
					AssetDatabase.Refresh();
				}
			}
		}

		private void updateInput(bool fix) {
			if( input == null ) return;
			if( input.GetType() == typeof(Texture2D) && !mset.AssetUtil.isReadableFormat(input as Texture2D) ) {
				Texture2D tex2D = input as Texture2D;
				Debug.LogError("Unreadable texture compression format: " + tex2D.format );
				clear();
				return;
			} 
			if( input.GetType() == typeof(RenderTexture) ) {
				srInput = null;
				assetLinear = !RT.sRGB;
				if(fix) fixInput();
			} else {
				srInput = new SerializedObject(input);
				if(fix) fixInput();
				assetLinear = mset.AssetUtil.isLinear(srInput);
			}
			renderLinear = PlayerSettings.colorSpace == ColorSpace.Linear;			

			updateBuffers();
			updatePreview();
		}

		// FILE PATHS
		private void updatePath() {
			if(locked) return;

			//path
			if( fullPath.Length > 0 ) {
				dir = Path.GetDirectoryName(fullPath)+"/";
				prettyPath = dir + Path.GetFileNameWithoutExtension(fullPath);
			} else {
				dir = "";
				prettyPath = "[Probe]";
			}

			//name
			if( type == Type.INPUT ) {
				if( fullPath.Length == 0 ) {
					skyName = "Probe";
				} else {
					skyName = Path.GetFileNameWithoutExtension(fullPath);
				}
			} else {
				skyName = Path.GetFileNameWithoutExtension(fullPath);
			}
		}		
		private string suggestPath(string refPath, bool unique) {
			string refName = skyName;
			if( refName.Length == 0 ) refName = "untitled";
			if( refPath.Length == 0 ) refPath = "Assets/" + refName + ".cubemap";
			
			string postfix = "";
			string fileExt = "";
			switch(type) {
			case Type.INPUT:
				postfix = "_PROBE";
				fileExt = "cubemap";
				break;
			case Type.SKY:
				postfix = "_SKY";
				fileExt = "cubemap";
				break;
			case Type.DIM:
				postfix = "_DIFF";
				fileExt = "cubemap";
				break;
			case Type.SIM:
				if(this.sky && this.sky.IsProbe) postfix = "_PROBE";
				else  postfix = "_SPEC";
				fileExt = "cubemap";
				break;
			};

			//int dot = refPath.LastIndexOf(".");
			//string path = refPath.Substring(0,dot) + postfix + "." + fileExt;

			string refDir = Path.GetDirectoryName(refPath)+"/";
			if( inputGUI && inputGUI.skyName.Length > 0 ) refName = inputGUI.skyName;

			string path = refDir + refName + postfix + "." + fileExt;
			if( unique ) path = AssetDatabase.GenerateUniqueAssetPath(path);
			return path;
		}
		// find an output cube matching the input texture's path and a list of common, known postfixes
		private string findInput() {
			if(inputPath.Length == 0) return "";
			string searchPath = inputPath;
			int uscore = searchPath.LastIndexOf("_");
			if( uscore > 0 ) searchPath = searchPath.Substring(0,uscore);
			string foundPath = "";
			
			string[] allPaths = AssetDatabase.GetAllAssetPaths();
			string inDir = Path.GetDirectoryName(searchPath).ToLowerInvariant()+"/";
			string inName = Path.GetFileNameWithoutExtension(searchPath).ToLowerInvariant();
			
			switch( type ) {
			case Type.SIM:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_spec", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "specular", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "spec", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "sim", ref allPaths) ) {
					return foundPath;
				}
				break;
			case Type.DIM:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_diff", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "diffuse", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "diff", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "dim", ref allPaths) ) {
					return foundPath;
				}
				break;
			case Type.SKY:
				if( findAssetByPostfix( ref foundPath, ref inDir, ref inName, "_sky", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "skybox", ref allPaths) ||
					findAssetByPostfix( ref foundPath, ref inDir, ref inName, "sky", ref allPaths) ) {
					return foundPath;
				}
				break;
			}
			return "";
		}
		//Searches a list of asset paths looking for a directory, name, and postfix match. Returns full path of match by reference and true if found.
		private static bool findAssetByPostfix( ref string dstPath, ref string inDir, ref string inName, string postfix) {
			string [] allPaths = AssetDatabase.GetAllAssetPaths();
			return findAssetByPostfix( ref dstPath, ref inDir, ref inName, postfix, ref allPaths );
		}
		private static bool findAssetByPostfix( ref string dstPath, ref string inDir, ref string inName, string postfix, ref string[] allPaths) {
			string dir;
			string name;
			//search by name and dir
			for( int i = 0; i<allPaths.Length; ++i ) {
				if(allPaths[i].Length < 7) continue;
				dir = Path.GetDirectoryName(allPaths[i]).ToLowerInvariant()+"/";
				name = Path.GetFileNameWithoutExtension(allPaths[i]).ToLowerInvariant();
				if( dir == inDir && (inName + postfix) == name ) {
					dstPath = allPaths[i];
					return true;
				}
			}
			//search by name only
			for( int i = 0; i<allPaths.Length; ++i ) {
				if(allPaths[i].Length < 7) continue;
				name = Path.GetFileNameWithoutExtension(allPaths[i]).ToLowerInvariant();
				if( (inName + postfix) == name ) {
					dstPath = allPaths[i];
					return true;
				}
			}
			return false;
		}				
		// draw directional lights from the scene over a panoramic image
		private void drawLightGizmos( Rect rect ) {
			if(!lightGizmo) return;

			UnityEngine.Object[] lights = GameObject.FindObjectsOfType(typeof(Light));
			for( int i=0; i<lights.Length; ++i ) {
				Light L = (lights[i]) as Light;					
				if( L.type != LightType.Directional ) continue;
				if( L.enabled == false ) continue;
				
				bool selected = Selection.Contains(L.gameObject);
				Vector3 dir = -L.transform.forward;
				mset.SkyManager mgr = mset.SkyManager.Get();
				if(mgr && mgr.GlobalSky) {
					//Put everything in relation to the active sky.
					//NOTE: the active sky skybox may not be what's displayed in Skyshop's input preview
					dir = Quaternion.Inverse(mgr.GlobalSky.transform.rotation) * dir;
				}
				
				float u = 0f;
				float v = 0f;
				if( previewLayout == Layout.PANO ) {
					mset.Util.dirToLatLong(out u, out v, dir);
				} else {
					/*   |+y |
					  ___|___|___ ___
					 |-x |+z |+x |-z |
					 |___|___|___|___|
					     |-y |
					     |___| */

					ulong face = 0;
					mset.Util.cubeLookup(ref u, ref v, ref face, dir);					
					switch((CubemapFace)face) {
					case CubemapFace.NegativeX:
						v+=1f;
						break;
					case CubemapFace.PositiveZ:
						u+=1f;
						v+=1f;
						break;
					case CubemapFace.NegativeY:
						u+=1f;
						v+=2f;
						break;
					case CubemapFace.PositiveY:
						u+=1f;
						break;
					case CubemapFace.PositiveX:
						u+=2f;
						v+=1f;
						break;
					case CubemapFace.NegativeZ:
						u+=3f;
						v+=1f;
						break;
					};
					
					u /= 4f;
					if( previewLayout == Layout.ROW ) v -= 1f;
					else v /= 3f;
				}
				bool clampedLight = v < 0 || v > 1f;
				v = Mathf.Clamp01(v);
				
				Rect imgRect = rect;
				imgRect.x += u*rect.width;
				imgRect.y += v*rect.height;
				
				if( selected ) {
					imgRect.width = imgRect.height = 32;
				} else {
					imgRect.width = imgRect.height = 16;
				}
				if( clampedLight ) {
					imgRect.width *= 0.5f;
					imgRect.height *= 0.5f;
				}
				imgRect.x -= imgRect.width*0.5f;
				imgRect.y -= imgRect.height*0.5f;
				GUI.DrawTexture(imgRect, lightGizmo);
			}
		}
		private void drawButtons( int buttonHeight ){ 			
			
			string newTip = "Create a new target cubemap and add it to the project";
			string findTip = "Search project for existing target cubemap by Input Panorama name";
			string useInputTip = "Set this cubemap to be the Input Panorama";
			string clearTip = "Deselect target cubemap";
			string probeTip = "Create and render to a new cubemap from the point of view of a selected object. The cubemap can be used as input for IBL processing but is not saved to disk.\n\nIf HDR is checked, capture will be as RGBM-encoded HDR (See Skyshop.txt docs for details and limitations).";

			string blurTip = "Blur this cubemap using GPU convolution. \nNote: this process will overwrite the existing cubemap data.";
			string blurFreeTip = "GPU computation is a Unity Pro feature :(";
			string beastTip = "Export and send this image to the Beast Lightmapper to be used as global illumination\n\nRequires Beast settings file, see \"Beast Global Illum Options\" for more details";
			string reloadTip = "Reload cubemap asset and refresh the preview image (needed when returning from play-mode).";
			
			GUIStyle buttonStyle = new GUIStyle("Button");
			buttonStyle.padding.top = buttonStyle.padding.bottom = 0;
			buttonStyle.padding.left = buttonStyle.padding.right = 0;
							
			EditorGUI.BeginDisabledGroup(locked);
			EditorGUILayout.BeginHorizontal();
				bool bNew = false;
				bool bBlur = false;
				bool bProbe = false;
				bool bNone = false;
				bool bFind = false;
				bool bAsGI = false;
				bool bAsInput = false;
								
				//configure which buttons are present based on type	
				switch(type) {
				case Type.INPUT:
					bNone = true;
					bAsGI = true;
					break;
				case Type.SKY:
					bNew = !this.inspector;
					bNone = true;
					bFind = true;
					bAsGI = true;
					bBlur = !this.inspector;
					break;
				case Type.DIM:
					bNew = true;
					bNone = true;
					bFind = true;
					bAsGI = true;
					break;
				case Type.SIM:
					bNew = true;
					bNone = true;
					bFind = true;
					bAsGI = true;
					bProbe = this.inspector;
					bBlur = !this.inspector;
					break;
				};				
				
				//NEW
				if( bNew ) {
					//if( GUILayout.Button(new GUIContent("New",newTip), buttonStyle, GUILayout.Width(55), GUILayout.Height(buttonHeight)) ) {
					//	newCube();
					//}
					GUIContent content = new GUIContent("New",newTip);
					Rect faceRect = GUILayoutUtility.GetRect(content, buttonStyle, GUILayout.Width(55), GUILayout.Height(buttonHeight));
					if( GUI.Button(faceRect, content) ) {
						GenericMenu genericMenu = new GenericMenu();
						string[] names = new string[] {"32", "64", "128", "256", "512", "1024"};
						int[] sizes = new int[] {32, 64, 128, 256, 512, 1024};
						for(int i=0; i<names.Length; ++i) {
							genericMenu.AddItem(new GUIContent(names[i]), sizes[i]==256, newCubeCallback, sizes[i]);
						}
						genericMenu.DropDown(faceRect);
					}
				}

				if( bBlur ) {
					bool isPro = Application.HasProLicense();//InternalEditorUtility.HasPro();
					GUIContent content = new GUIContent("GPU Blur",isPro ? blurTip : blurFreeTip);
					Rect faceRect = GUILayoutUtility.GetRect(content, buttonStyle, GUILayout.Width(75), GUILayout.Height(buttonHeight));
					EditorGUI.BeginDisabledGroup(cube == null || !isPro);
						bool blurPressed = GUI.Button(faceRect, content);
					EditorGUI.EndDisabledGroup();
					if( blurPressed ) {
						GenericMenu genericMenu = new GenericMenu();
						string[] names = new string[] {"Low", "Medium", "Blurriest"};
						int[] sizes = new int[] {4096,1024,64};
						for(int i=0; i<names.Length; ++i) {
							genericMenu.AddItem(new GUIContent(names[i]), false, blurCubeCallback, sizes[i]);
						}
						genericMenu.DropDown(faceRect);
					}
				}


				//NEW PROBE
				if( bProbe ) {
					bool disabled = this.cube == null;
					//disabled in Skyshop proper in Unity Free

					string notProbeTitle = "Sky is not marked as probe!";
					string notProbeText = "Would you like to change this sky to a probe and overwrite its cubemaps? This process cannot be undone.";

					EditorGUI.BeginDisabledGroup(disabled);
					GUIContent content = new GUIContent("Probe Direct", probeTip);
					if( GUILayout.Button(content, buttonStyle, GUILayout.Width(85), GUILayout.Height(buttonHeight)) ) {
						bool cap = true;
						if(sky && !sky.IsProbe) {
							cap = EditorUtility.DisplayDialog(notProbeTitle, notProbeText, "Capture", "Cancel");
						}
						if(cap) {
							if(sky) sky.IsProbe = true;
							mset.SkyManager mgr = SkyManager.Get();
							if(mgr) mgr.ProbeExposures = Vector4.zero;
							renderProbeCallback(this.cube.width, false);
						}
					}
					content = new GUIContent("Probe Direct+IBL", probeTip);
					if( GUILayout.Button(content, buttonStyle, GUILayout.Width(112), GUILayout.Height(buttonHeight)) ) {
						bool cap = true;
						if(sky && !sky.IsProbe) {
							cap = EditorUtility.DisplayDialog(notProbeTitle, notProbeText, "Capture", "Cancel");
						}
						if(cap) {
							if(sky) sky.IsProbe = true;
							mset.SkyManager mgr = SkyManager.Get();
							if(mgr) mgr.ProbeExposures = Vector4.one;
							renderProbeCallback(this.cube.width, true);
						}
					}
				}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
				//RELOAD
				EditorGUI.BeginDisabledGroup(this.fullPath.Length == 0);
				if (GUILayout.Button(new GUIContent("Reload", reloadTip), buttonStyle, GUILayout.Width(55), GUILayout.Height(buttonHeight))) {
					reloadReference();
				}
				EditorGUI.EndDisabledGroup();
				//FIND
				if( bFind ) {
					EditorGUI.BeginDisabledGroup( inputPath.Length == 0 || (sky && sky.IsProbe));
					if( GUILayout.Button(new GUIContent("Find",findTip), buttonStyle, GUILayout.Width(50), GUILayout.Height(buttonHeight)) ) {
						find();
					}
					EditorGUI.EndDisabledGroup();
				}
			
				//NONE
				if( bNone ) {
					if( GUILayout.Button(new GUIContent("None",clearTip), buttonStyle, GUILayout.Width(50), GUILayout.Height(buttonHeight)) ) {						
						clear();
					}
				}
				//USE AS INPUT
				if( bAsInput ) {
					EditorGUI.BeginDisabledGroup(this.input == null);
					if( GUILayout.Button(new GUIContent("Use as Input",useInputTip), buttonStyle, GUILayout.Width(85), GUILayout.Height(buttonHeight)) ) {
						if( this.inputGUI != null ) {
							this.inputGUI.HDR = this.HDR;
							this.inputGUI.setReference(AssetDatabase.GetAssetPath(this.input), false, true);
						}
					}
					EditorGUI.EndDisabledGroup();
				}
				
				//USE AS GI
				if( bAsGI ) {
					EditorGUI.BeginDisabledGroup(this.input == null || !BeastConfig.hasConfigFile());
					GUIContent content = new GUIContent("Use as GI",beastTip);
					Rect giRect = GUILayoutUtility.GetRect(content, buttonStyle, GUILayout.Width(80), GUILayout.Height(buttonHeight));					
					if( GUI.Button(giRect, content) ) {
					//if( GUILayout.Button(content, buttonStyle, GUILayout.Width(75), GUILayout.Height(buttonHeight)) ) {
						if( type == Type.SIM && this.mipmapped ) {
							GenericMenu genericMenu = new GenericMenu();
							string[] names = new string[] {"Mip 0", "Mip 1", "Mip 2", "Mip 3"};
							genericMenu.AddItem(new GUIContent(names[0]), true, exportBufferCallback, 0);
							genericMenu.AddItem(new GUIContent(names[1]), false, exportBufferCallback, 1);
							genericMenu.AddItem(new GUIContent(names[2]), false, exportBufferCallback, 2);
							genericMenu.AddItem(new GUIContent(names[3]), false, exportBufferCallback, 3);
							genericMenu.DropDown(giRect);
						} else {
							exportBufferCallback(0);
						}
					}
					EditorGUI.EndDisabledGroup();
				}
				
			EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup(); //end locked
		}

		private void newCubeCallback(object data) {
			//inspector mode? you're probably making a probe
			if(this.inspector && this.sky) this.sky.IsProbe = true;

			int faceSize = (int)data;
			if( faceSize == -1 ) {
				newCube();
			}
			else {
				newCube(faceSize);
			}
			queuedPath = fullPath;
		}

		private void blurCubeCallback(object data) {
			Cubemap tempCube = new Cubemap(this.cube.width, TextureFormat.ARGB32, false);
			for(int i=0; i<6; ++i) {
				CubemapFace face = (CubemapFace)i;
				tempCube.SetPixels(this.cube.GetPixels(face), face);
			}
			tempCube.Apply(false);
			mset.AssetUtil.setLinear(new SerializedObject(tempCube), false);

			mset.SkyProbe p = new mset.SkyProbe();
			p.maxExponent = (int)data;
			p.convolutionScale = 1f;
			p.highestMipIsMirror = false;
			p.generateMipChain = false;
			bool linear = UnityEditor.PlayerSettings.colorSpace == ColorSpace.Linear;

			Undo.RegisterCompleteObjectUndo(this.cube, "cubemap blur");
			p.blur(this.cube, tempCube, this.HDR, this.HDR, linear);
			queuedPath = fullPath;
		}

		public void renderProbeCallback(object data, bool probeIBL) { renderProbeCallback(data, probeIBL, null); }
		public void renderProbeCallback(object data, bool probeIBL, GameObject selection) {
			int faceSize = (int)data;
			if( faceSize == -1 ) return;
			if( this.cube == null ) return;
	
			//when a sky is selected, use play-mode capture probe
			mset.SkyManager mgr = mset.SkyManager.Get();
			mset.Sky sky = null;
			if(Selection.activeGameObject) sky = Selection.activeGameObject.GetComponent<mset.Sky>();

			//send call through SkyManager and play-mode
			if(mgr && sky != null) {
				mset.Sky[] list = new mset.Sky[1];
				list[0] = sky;
				Probeshop.ProbeSkies(null, list, false, probeIBL, null);
			}
			//otherwise, if Unity Pro, use SkyProbe directly in editor
			/*else if(UnityEditorInternal.InternalEditorUtility.HasPro() && PlayerSettings.useDirect3D11) {
				SkyProbe probe = new SkyProbe();
				Cubemap targetCube = this.cube;

				//if( targetCube == null ) {
				//	targetCube = new Cubemap(faceSize, TextureFormat.ARGB32, this.mipmapped);
				//}
				probe.cubeRT = new RenderTexture(faceSize, faceSize, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.sRGB);				
				probe.cubeRT.dimension = UnityEngine.Rendering.TextureDimension.Cube;
				probe.cubeRT.useMipMap = this.mipmapped;
				probe.cubeRT.generateMips = this.mipmapped;

				Transform at = Selection.activeTransform;
				if( at == null ) at = Camera.main.transform;
				if(selection != null) 
				{
					at = selection.transform;
				}
				probe.capture(targetCube, at.position, at.rotation, this.HDR, this.renderLinear, this.type == Type.SIM);

				SerializedObject srTarget = new SerializedObject(targetCube);
				mset.AssetUtil.setLinear(srTarget, false);
				updateInput(false);
				RenderTexture.DestroyImmediate(probe.cubeRT);
			}*/
			else
			{
				Debug.LogError("Sky Manager not found in the scene, it is required for probe capture.");
			}
		}
				
		private void exportBufferCallback(object data) {
			int mip = (int)data;
			if( mip == -1 ) return;
			
			SerializedObject srConfig = BeastConfig.getSerializedConfig();
			string beastName = "BeastSky_noimport";
			string dir = BeastConfig.configFilePath;
			
			if(!string.IsNullOrEmpty(dir)) {
				updateBuffers();				
				if( buffers[mip].empty() ) mip = 0;
				if( buffers[mip].empty() ) return;
				string path = Path.GetFullPath( Path.GetDirectoryName(dir) + "/" + beastName + ".hdr" );
				path = path.Replace('\\','/'); //stop it, windows.				
				int width = this.buffers[mip].width*4;
				int height = this.buffers[mip].width*2;
				Color[] pixels = new Color[width*height];
				this.buffers[mip].toPanoBuffer(ref pixels, width, height);
				HDRProcessor.writeHDR(ref pixels, width, height, path);
											
				SerializedProperty iblImageFile =  srConfig.FindProperty ("config.environmentSettings.iblImageFile");
				SerializedProperty giEnvironment = srConfig.FindProperty ("config.environmentSettings.giEnvironment");
				iblImageFile.stringValue = path;
				giEnvironment.intValue = (int)ILConfig.EnvironmentSettings.Environment.IBL;
				srConfig.ApplyModifiedProperties();
				
				BeastConfig.SaveConfig();
			}
		}
		
		public void update() {
			bool dirtyPreview = false;
			bool dirtyBuffers = false;
			updateProperties(ref dirtyPreview, ref dirtyBuffers);

			if( dirtyBuffers ) updateBuffers();
			if( dirtyPreview ) updatePreview();
		}
		
		private void updateProperties(ref bool dirtyPreview, ref bool dirtyBuffers) {
			if( locked ) return;
			//If Unity is configured to render in linear space, the gui preview texture data is expected to be in sRGB space
			bool prevLinear = renderLinear;
			renderLinear = PlayerSettings.colorSpace == ColorSpace.Linear;
			if( prevLinear != renderLinear ) {
				dirtyPreview = true;
			}
			if(input == null) srInput = null;
			if(srInput != null) {
				srInput.UpdateIfRequiredOrScript();
				//If the cubemap asset is changed to be "Linear" aka "Bypass sRGB Sampling", we need to redo our preview and reimport the buffer
				prevLinear = assetLinear;
				assetLinear = mset.AssetUtil.isLinear(srInput);
				if( prevLinear != assetLinear ) {
					dirtyPreview = true;
					dirtyBuffers = true;
				}
			}
		}
		
		// GUI
		public static void drawStaticGUI() {
			//TODO: until bicubic is figured out properly, this is pulled.
			/*sFilterMode = (CubeBuffer.FilterMode)EditorGUILayout.EnumPopup(
				new GUIContent("Filtering Mode","Resampling method for upscaling or downscaling image data."),
				sFilterMode,
				GUILayout.Width(300)
			);*/
		}
		private void applyStaticGUI() {
			if( locked ) return;
			if( buffers != null ) //temp
			for(int i=0; i<buffers.Length; ++i) {
				buffers[i].filterMode = sFilterMode;
			}
		}
		public void drawGUI() {
			//references cannot be fully set in some callbacks, this allows for setReference to be queued until the next repaint
			if(queuedPath.Length > 0 ) {
				setReference(queuedPath, mipmapped);
				queuedPath = "";
			}
			EditorGUI.BeginDisabledGroup(locked);
			
			applyStaticGUI();
			string label = "";
			string previewLabel = "";
			string toolTip = "";
			
			bool dirtyPreview = false;
			bool dirtyBuffers = false;
			
			updateProperties(ref dirtyPreview, ref dirtyBuffers);
			
			switch(type) {
			case Type.INPUT:
				label = "INPUT PANORAMA";
				toolTip = "Input Panorama - \nCubemap, panorama, or horizontal-cross image of the sky that will light the scene.";
				previewLabel = "Input Panorama";
				break;
			case Type.SKY:
				label = "SKYBOX";
				toolTip = "Skybox - \nTarget cubemap to be used as the scene background.";
				previewLabel = "Skybox Preview";
				break;
			case Type.DIM:
				label = "DIFFUSE OUTPUT";
				toolTip = "Diffuse Output -\nTarget cubemap for storing diffuse lighting data.";
				previewLabel = "Diffuse Preview";
				break;
			case Type.SIM:
				label = "SPECULAR OUTPUT";
				toolTip = "Specular Output -\nTarget cubemap for storing specular lighting data.";
				previewLabel = "Specular Preview";
				break;
			};
			EditorGUILayout.BeginHorizontal();{
				Texture prev = input;
				if( allowTexture  || allowRenderTexture) {
					input = (Texture)EditorGUILayout.ObjectField(input, typeof(Texture), false, GUILayout.Width(57), GUILayout.Height(57));
				} else {
					cube = (Cubemap)EditorGUILayout.ObjectField(cube, typeof(Cubemap), false, GUILayout.Width(57), GUILayout.Height(57));
				}

				RT = input as RenderTexture;
				cube = input as Cubemap;

				// new cubemap target
				if(input != prev) {
					if( RT != null ) {
						fullPath = "";
						setReference(RT,mipmapped,true);
					} else {
						fullPath = AssetDatabase.GetAssetPath(input);
						setReference(fullPath,mipmapped,true);
					}
				}
				
				EditorGUILayout.BeginVertical(); {
					EditorGUILayout.LabelField(new GUIContent(label, toolTip), GUILayout.Height(14));

					EditorGUI.BeginDisabledGroup(true);
						EditorGUILayout.LabelField(prettyPath, GUILayout.Height(14));
					EditorGUI.EndDisabledGroup();					
					EditorGUILayout.Space();
					drawButtons(15);					
				}EditorGUILayout.EndVertical();	
			}EditorGUILayout.EndHorizontal();
			
			float offset = 0f;
			if( inspector ) offset = 4f;
			if( previewLayout == Layout.CROSS ) {
				if( input )	previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, 3f*previewWidth/4f, previewLabel, preview, false);
				else 		previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, 3f*previewWidth/4f, previewLabel, null, false);
			} else if( previewLayout == Layout.PANO ) {
				if( input )	previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/2f, previewLabel, preview, false);
				else 		previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/2f, previewLabel, null, false);
			} else {
				if( input )	previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/4f, previewLabel, preview, false);
				else 		previewRect = mset.EditorUtil.GUILayout.drawTexture(offset, 0, (float)previewWidth, previewWidth/4f, previewLabel, null, false);
			}
			
			//draw light overlay
			if(showLights) drawLightGizmos(previewRect);
			
			int buttonHeight = 16;
			Rect previewBar = GUILayoutUtility.GetRect(320, 0);
			previewBar.x += offset+2;
			previewBar.y -= buttonHeight+2;
			previewBar.width = 60;
			previewBar.height = 16;
			GUI.Box(previewBar,"","HelpBox");
			previewBar.width = previewWidth;
			
			previewBar.y -= 1;
			Rect rect = previewBar;
			rect.x += 2;
			rect.width = 42;
			rect.height = 16;
			
			//if HDR was toggled, we need to reimport the buffers and recompute our preview
			string HDRTip;
			if( inspector ) {
				HDRTip = "Treat cubemap as RGBM-encoded HDR data.";
			} else {
				if( type == Type.INPUT ) HDRTip = "Treat input as RGBM-encoded HDR image.";
				else 					 HDRTip = "Write output as RGBM-encoded HDR image.";
			}
			
			bool newHDR = GUI.Toggle(rect, HDR, new GUIContent("HDR", HDRTip));
			if( newHDR != HDR ) {
				HDR = newHDR;
				dirtyBuffers = true;
				dirtyPreview = true;
			}
				
			rect = previewBar;
			rect.x += previewBar.width - 15;
			rect.width = 20;
			
			bool pick = false;
			if( showLights ) pick = mset.EditorUtil.GUILayout.tinyButton(rect.x-53, rect.y+1, pickerIcon, "Pick light color from image", -1,1);
			showLights = mset.EditorUtil.GUILayout.tinyToggle(rect.x-40, rect.y+1, "*", "Show/Hide directional lights", 1, 3, showLights);
			bool R = mset.EditorUtil.GUILayout.tinyButton(rect.x - 24, rect.y + 1, "R", "Row preview", 1, 1);
			bool P = mset.EditorUtil.GUILayout.tinyButton(rect.x-12, rect.y+1, "P", "Panorama preview", 1, 1);
			bool C = mset.EditorUtil.GUILayout.tinyButton(rect.x-0,  rect.y+1, "C", "Cross preview", 0, 1);

			if( R ) {
				previewLayout = Layout.ROW;
				dirtyPreview = true;
			}
			if( P ) {
				previewLayout = Layout.PANO;
				dirtyPreview = true;
			}
			if( C ) {
				previewLayout = Layout.CROSS;
				dirtyPreview = true;
			}

			if( pick ) {
				Light[] lights = new Light[Selection.transforms.Length];
				int lightCount = 0;
				for( int i=0; i<Selection.transforms.Length; ++i ) {
					Light L = Selection.transforms[i].gameObject.GetComponent<Light>();
					if( L == null || L.type != LightType.Directional ) continue;					
					lights[lightCount] = L;
					lightCount++;
				}
				mset.EditorUtil.RegisterUndo(lights, "Light Color Picking");
				for( int i=0; i<lightCount; ++i ) {
					Light L = lights[i];
					float u=0f;
					float v=0f;
					ulong face = 0;
					mset.Util.cubeLookup(ref u, ref v, ref face, -L.transform.forward);
					Color pixel = Color.black;
					//TODO: multisampling
					buffers[0].sampleNearest(ref pixel, u, v, (int)face);
					float intens = Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b));
					if( intens > 1f ) {
						pixel /= intens;
						L.intensity = intens;
					} else {
						L.intensity = 1f;
					}
					//Q: should light color be gamma or linear? A: linear, so pull it straight from buffer[0]
					//if( !assetLinear ) mset.Util.applyGamma(ref pixel, Gamma.toSRGB);
					L.color = pixel;
				}
			}
			if( this.type == Type.INPUT ) {
				string nameTip = "Panorama Name - \nString used when name new output cubemap assets.";
				skyName = EditorGUILayout.TextField(new GUIContent("Panorama Name", nameTip), skyName, GUILayout.Width(332));
			}

			if( dirtyBuffers ) updateBuffers();
			if( dirtyPreview ) updatePreview();
			EditorGUI.EndDisabledGroup();
		}
				
		// PREVIEW
		public void updateBuffers() {
			if(locked || !input) return;
			if(srInput != null) srInput.Update();
			bool splitMips = mipmapped && input.width>8 && input.height>8 && mset.AssetUtil.isMipmapped(srInput);
			
			//HACK if the input is a sky, limit its size to a height of 256, just to keep the preview image under control
			int mip = 0;
			if( type==Type.SKY && input.height > 256 && splitMips ) {
				mip = QPow.Log2i(cube.width) - 8; //find the mip for 256x256
			}
			
			bool useGamma = !assetLinear;
			if( input.GetType() == typeof(Cubemap) ) {
				cube = (Cubemap)input;
				buffers[0].fromCube(cube, mip, colorMode, useGamma);
				if(splitMips) {
					buffers[1].fromCube(cube, 1, colorMode, useGamma);
					buffers[2].fromCube(cube, 2, colorMode, useGamma);
					buffers[3].fromCube(cube, 3, colorMode, useGamma);
				} else {
					buffers[1].clear();
					buffers[2].clear();
					buffers[3].clear();
				}
			}
			else if( input.GetType() == typeof(Texture2D) ) {
				string ipath = AssetDatabase.GetAssetPath(input);
				TextureImporter ti = mset.AssetUtil.getTextureImporter(ipath, "CubemapGUI");
				if( ti != null ) {
					ti.npotScale = TextureImporterNPOTScale.None;
					ti.isReadable = true;
					AssetDatabase.ImportAsset(ipath);
				}
				//horizontal cross
				if( 4*input.height == 3*input.width ) {
					//TODO: something past this point causes horizontal cross LDR images to break if they are not in linear color-space
					buffers[0].fromHorizCrossTexture((Texture2D)input, 0, colorMode, useGamma);
					if(splitMips) {
						//TODO: no mips on non PoT textures (ie 4:3 anything)
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
				//vertical column
				else if( 1*input.height == 6*input.width ) {
					buffers[0].fromColTexture((Texture2D)input, mip, colorMode, useGamma);
					if(splitMips) {
						//no mips on non PoT textures (ie 6:1 anything)
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
				//panorama
				else {
					int faceSize = Mathf.NextPowerOfTwo(input.height); // how big we want to make our cubemap
					if( type == Type.INPUT ) faceSize = Mathf.Min(faceSize,1024);	//allow for bigger input buffer, this is what gets used for computation
					else 					 faceSize = Mathf.Min(faceSize,256);	//other types only use the buffers for previewing, 256 per face is fine
					buffers[0].fromPanoTexture((Texture2D)input, faceSize, colorMode, useGamma);
					if(splitMips) {
						//TODO: no mip support in fromPano right now!
						buffers[1].clear();
						buffers[2].clear();
						buffers[3].clear();
					}
				}
			} else if( input.GetType () == typeof(RenderTexture) ) {
				//TODO
				buffers[0].clear();
				buffers[1].clear();
				buffers[2].clear();
				buffers[3].clear();
			} else {
				Debug.LogError("Invalid texture type for CubeGUI input!");
			}

			if( this.computeSH ) {
				SH.clearToBlack();
				//try a low mip to project from first
				if(!this.buffers[3].empty())SHUtil.projectCubeBuffer(ref SH, this.buffers[3]);
				else 						SHUtil.projectCubeBuffer(ref SH, this.buffers[0]);
				SHUtil.convolve( ref SH );
			}
		}
		
		public void updatePreview() {
			if( locked || !input ) return;
			if( input.GetType() == typeof(RenderTexture)) return; //TODO

			int previewFaceSize = previewWidth/4;
			int previewHeight = previewFaceSize;
			
			switch( previewLayout ) {
			case Layout.ROW:
				previewHeight = previewFaceSize;
				break;
			case Layout.CROSS:
				previewHeight = 3*previewFaceSize;
				break;
			case Layout.PANO:
				previewHeight = 2*previewFaceSize;
				break;
			};
			
			if( !preview ) {
				preview = new Texture2D(previewWidth, previewHeight,TextureFormat.ARGB32, false, true);
				preview.hideFlags |= HideFlags.DontSave; //do not save this and do not clear it coming back from play mode
			}
			else if( preview.width != previewWidth || preview.height != previewHeight ) {
				preview.Resize(previewWidth, previewHeight);
			}
			if( previewLayout == Layout.CROSS ) {
				mset.Util.clearTo2D(ref preview, Color.black);
			}
			
			int faceSize = buffers[0].faceSize;
			
			//many input formats don't have mipmapped buffers, sample high mip
			bool splitMips = mipmapped &&
							faceSize>8 &&
							!buffers[1].empty() &&
							!buffers[2].empty() &&
							!buffers[3].empty();
			
			
			float gamma = 1f;
			if(!assetLinear) {//renderLinear) {
				//if linear-space rendering, we're expecting a gamma curve to be applied to the image before display
				gamma *= Gamma.toSRGB;
			}
	
			if( previewLayout == Layout.PANO ) {
				ulong w = (ulong)previewWidth;
				ulong h = (ulong)previewHeight;
				
				Color pixel = new Color();
				for(ulong x=0; x<w; ++x)
				for(ulong y=0; y<h; ++y) {
					float u=0f;
					float v=0f;
					ulong face=0;
					mset.Util.latLongToCubeLookup(ref u, ref v, ref face, x, y, w, h);
					int mip=0;
					if( splitMips ) mip = (int)(x*4/w);
					buffers[mip].sample(ref pixel, u, v, (int)face);
					pixel.r = Mathf.Clamp01(pixel.r);
					pixel.g = Mathf.Clamp01(pixel.g);
					pixel.b = Mathf.Clamp01(pixel.b);
					if( gamma != 1f ) {  mset.Util.applyGamma(ref pixel, gamma); }
					preview.SetPixel((int)x,(int)y,pixel);
				}
				preview.Apply();
			} else {
				Color[] facePixels = new Color[previewFaceSize * previewFaceSize];
				int faceCount;
				if( previewLayout == Layout.CROSS )	faceCount = 6;
				else  								faceCount = 4;
				for( int face = 0; face<faceCount; ++face ) {
					int mip = 0;
					if( splitMips ) {
						mip = face;
						if( face >= 4 ) mip = 1;
					}
					int sampleFace = face;
					switch(face) {
						case 0: sampleFace = (int)CubemapFace.NegativeX; break;
						case 1: sampleFace = (int)CubemapFace.PositiveZ; break;
						case 2: sampleFace = (int)CubemapFace.PositiveX; break;
						case 3: sampleFace = (int)CubemapFace.NegativeZ; break;
						case 4: sampleFace = (int)CubemapFace.PositiveY; break;
						case 5: sampleFace = (int)CubemapFace.NegativeY; break;
					};
					if( buffers[mip].pixels == null ) {
						Debug.LogError("null pixels on mip " + mip);
					}
					buffers[mip].resampleFace(ref facePixels, sampleFace, previewFaceSize, true);
					CubeBuffer.encode(ref facePixels, facePixels, ColorMode.RGB8, false);
					if( gamma != 1f ) { mset.Util.applyGamma(ref facePixels, gamma); }
					
					if( previewLayout == Layout.CROSS ) {
						if( face < 4 )			preview.SetPixels( previewFaceSize*face, 1*previewFaceSize, previewFaceSize, previewFaceSize, facePixels );
						else if( face == 4 )	preview.SetPixels( previewFaceSize*1,    2*previewFaceSize, previewFaceSize, previewFaceSize, facePixels );
						else if( face == 5 )	preview.SetPixels( previewFaceSize*1,    0,					previewFaceSize, previewFaceSize, facePixels );
					} else {
						preview.SetPixels( previewFaceSize*face, 0, previewFaceSize, previewFaceSize, facePixels );
					}
				}
				preview.Apply();
			}
		}
	};
}