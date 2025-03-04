﻿// Marmoset Skyshop
// Copyright 2014 Marmoset LLC
// http://marmoset.co

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using mset;

namespace mset {
	[CustomEditor(typeof(SkyManager))]
	public class SkyManagerInspector : Editor {
		public void OnEnable() {
			mset.SkyManager mgr = target as mset.SkyManager;
			if(mgr.GlobalSky == null) {
				mgr.GlobalSky = GameObject.FindObjectOfType<mset.Sky>();
			}
			mgr.EditorUpdate(true);
		}
		
		public override void OnInspectorGUI() {

			EditorGUI.BeginChangeCheck();
			Undo.RecordObject(target, "Sky Manager Change");

			mset.SkyManager skmgr = target as mset.SkyManager;
			mset.Sky nusky = EditorGUILayout.ObjectField("Global Sky", skmgr.GlobalSky, typeof(mset.Sky), true) as mset.Sky;
			if(skmgr.GlobalSky != nusky) {
				//TODO: is this necessary?
				if(!Application.isPlaying && nusky != null) {
					nusky.Apply();
				}
				if(nusky == null) {
					RenderSettings.skybox = null;
				}
				skmgr.GlobalSky = nusky;
			}

			skmgr.ShowSkybox = GUILayout.Toggle(skmgr.ShowSkybox, new GUIContent("Show Skybox", "Toggles rendering the global sky's background image in both play and edit modes"));

			EditorGUILayout.Space();
			skmgr.ProjectionSupport = GUILayout.Toggle(skmgr.ProjectionSupport, new GUIContent("Box Projection Support", "Optimization for disabling all box projected cubemap distortion at the shader level"));
			skmgr.BlendingSupport =	GUILayout.Toggle(skmgr.BlendingSupport, new GUIContent("Blending Support","Optimization for disabling blending transitions between skies at the shader level"));
			skmgr.LocalBlendTime = EditorGUILayout.FloatField( "Local Sky Blend Time", skmgr.LocalBlendTime);
			skmgr.GlobalBlendTime = EditorGUILayout.FloatField( "Global Sky Blend Time", skmgr.GlobalBlendTime);
			EditorGUILayout.Space();


			GUILayout.BeginHorizontal();
			skmgr.GameAutoApply = GUILayout.Toggle(skmgr.GameAutoApply, new GUIContent("Auto-Apply in Game", "If enabled for game mode, Sky Manager will keep and constantly update a list of dynamic renderers in the scene, applying local skies to them as they move around.\n\nRequired for dynamic sky binding and Sky Applicator triggers.\n\nNOTE: This feature causes material instances to be spawned at runtime and may hurt render batching."), GUILayout.Width(128));
			EditorGUI.BeginDisabledGroup(true);
			GUILayout.Label("(Creates material instances)");
			EditorGUI.EndDisabledGroup();
			GUILayout.EndHorizontal();

			skmgr.EditorAutoApply = GUILayout.Toggle(skmgr.EditorAutoApply, new GUIContent("Auto-Apply in Editor","If enabled for edit mode, Sky Manager will apply local skies to renderers contained in their Sky Applicator trigger volumes.\n\nAffects editor viewport only."));
			skmgr.AutoMaterial = GUILayout.Toggle (skmgr.AutoMaterial, new GUIContent("Dynamic Materials", "Periodically update the material caches in Sky Anchors. Enable if material lists of renderers are going to change at runtime (e.g. adding, removing, or replacing material references of renderers, property changes won't matter)."));

			//NOTE: The _ vars are stored in sky manager because they're part of the saved state. Pulling the list of layers from the bit mask instead of a full int array would sort the layer list every frame.
			skmgr._IgnoredLayerCount = EditorGUILayout.IntField("Ignored Layer Count", skmgr._IgnoredLayerCount);

			//if never allocated before, allocate ignoredLayers list here, it's only ever used here to display and configure the true hero: IgnoredLayerMask
			if(skmgr._IgnoredLayers == null) skmgr._IgnoredLayers = new int[32];

			skmgr.IgnoredLayerMask = 0;
			for(int i=0; i<skmgr._IgnoredLayerCount; ++i) {
				skmgr._IgnoredLayers[i] = EditorGUILayout.LayerField(" ", skmgr._IgnoredLayers[i]);
				skmgr.IgnoredLayerMask |= 1 << skmgr._IgnoredLayers[i];
			}

			GUILayout.BeginHorizontal();
			if(GUILayout.Button(new GUIContent("Preview Auto-Apply","Updates editor viewport to show an accurate representation of which renderers will be bound to which skies in the game.\n\nEditor Auto-Apply performs this every frame."), GUILayout.Width(140))) {
				skmgr.EditorUpdate(true);
				SceneView.RepaintAll();
			}
			GUILayout.EndHorizontal();


			EditorGUILayout.Space();

			string tipExponent = "Highest gloss exponent use in the specular mip chain when capturing probes. Other exponents in the chain are generated from this value.";
			skmgr.ProbeExponent = EditorGUILayout.IntField(new GUIContent("Probe Specular Exponent", tipExponent), skmgr.ProbeExponent);
			skmgr.ProbeExponent = Mathf.Max(1, skmgr.ProbeExponent);

			string staticTip = "If enabled, only GameObjects marked as \"Static\" will be rendered when capturing cubemaps.";
			skmgr.ProbeOnlyStatic = GUILayout.Toggle(skmgr.ProbeOnlyStatic, new GUIContent("Probe Only Static Objects", staticTip));

			#if UNITY_4_3 || UNITY_4_5 || UNITY_4_6
				string dx11Tip = "Uses HDR render-textures to capture sky probes faster and with better quality.\n\nRequires project to be in Direct3D 11 mode while capturing.";
				if(PlayerSettings.useDirect3D11) {
					skmgr.ProbeWithCubeRT = GUILayout.Toggle(skmgr.ProbeWithCubeRT, new GUIContent("Probe Using Render-to-Cubemap",dx11Tip));
				} else {
					EditorGUI.BeginDisabledGroup(true);
					GUILayout.Toggle(false, new GUIContent("Probe Using Render-to-Cubemap (Requires Direct3D11)",dx11Tip));
					EditorGUI.EndDisabledGroup();
				}
			#else
				//All platforms and both free and pro versions of Unity support render to cubemap.
				skmgr.ProbeWithCubeRT = true;

				//string RTTip = "Uses HDR render-textures to capture sky probes faster and with better quality.";
				//skmgr.ProbeWithCubeRT = GUILayout.Toggle(skmgr.ProbeWithCubeRT, new GUIContent("Probe Using Render-to-Cubemap",RTTip));
			#endif

			string camTip = "Sky probing is performed using the settings and clipping planes of this camera. If field is empty, Main Camera is used.";
			skmgr.ProbeCamera = EditorGUILayout.ObjectField( new GUIContent("Probe with Camera", camTip), skmgr.ProbeCamera, typeof(Camera), true ) as Camera;
			

			GUILayout.BeginHorizontal();
			if(GUILayout.Button(new GUIContent("Probe Skies (Direct)"), GUILayout.Width(140))) {
				bool probeNonProbes = false;
				bool probeIBL = false;
				Probeshop.ProbeSkies( null, GameObject.FindObjectsOfType<mset.Sky>(), probeNonProbes, probeIBL, null);
			}
			if(GUILayout.Button("Probe Skies (Direct+IBL)", GUILayout.Width(170))) {
				bool probeNonProbes = false;
				bool probeIBL = true;
				Probeshop.ProbeSkies( null, GameObject.FindObjectsOfType<mset.Sky>(), probeNonProbes, probeIBL, null);
			}			
			GUILayout.EndHorizontal();

			if( EditorGUI.EndChangeCheck() ) {
				
				skmgr.EditorUpdate(true);
				EditorUtility.SetDirty(target);
				EditorSceneManager.MarkSceneDirty(skmgr.gameObject.scene);
				SceneView.RepaintAll();
			}
		}
	}
}