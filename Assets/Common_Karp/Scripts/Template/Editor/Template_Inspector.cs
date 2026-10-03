#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

//[CanEditMultipleObjects]
//[CustomEditor(typeof(Template))]
public class Template_Inspector : Editor
{
	SerializedProperty prop;
	ReorderableList allChunk;

	// Start like
	private void OnEnable()
	{
		//Link variables to property
		//prop = serializedObject.FindProperty(nameof(Script.prop));
		#region reordable list
		//Initialise la liste
		allChunk = new ReorderableList(serializedObject, prop, true, true, true, true);
		//linker à une serielizedProperty (une collection, array, liste,etc)
		//Preparer les callbacks
		allChunk.drawElementCallback += ElementDrawer;
		allChunk.drawHeaderCallback += HeaderDrawer;
		allChunk.onAddCallback += AddCallBack;
		allChunk.onAddDropdownCallback += AddDropDownCallBack;
		allChunk.onRemoveCallback += RemoveCallBack;
		allChunk.onReorderCallback += ReorderCallback;
		allChunk.elementHeightCallback += ElementHeightCallBack;
	}


	void HeaderDrawer(Rect rect)
	{
		EditorGUI.LabelField(rect, "Titre de la liste");
	}
	void ElementDrawer(Rect rect, int index, bool isActive, bool isFocused)
	{
		EditorGUI.PropertyField(rect, prop.GetArrayElementAtIndex(index));
	}
	void AddCallBack(ReorderableList rList)
	{

	}
	private void AddDropDownCallBack(Rect buttonRect, ReorderableList list)
	{

	}
	void RemoveCallBack(ReorderableList rList)
	{
		prop.DeleteArrayElementAtIndex(rList.index);
	}
	void ReorderCallback(ReorderableList rList)
	{

	}
	float ElementHeightCallBack(int index)
	{
		float numberOfLine = EditorGUIUtility.currentViewWidth < 334 ? 2 : 1;
		return (EditorGUIUtility.singleLineHeight * numberOfLine) + 1;
	}
	#endregion

	// Update like
	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		#region Scope
		//Button
		if (GUILayout.Button(new GUIContent("Voici un button", "Tips"))) { }

		#region EditorGUI
		//Change Check
		using (var check = new EditorGUI.ChangeCheckScope())
		{
			if (check.changed)
			{
			}
		}

		//Disable
		using (new EditorGUI.DisabledScope(false))
		{
		}

		//Disable Groupe
		bool variable = false;
		variable = EditorGUILayout.Toggle("CanVar", variable);
		using (var check = new EditorGUI.DisabledGroupScope(variable)) { }

		// Indent block
		using (new EditorGUI.IndentLevelScope()) { }

		// Scroll view
		Vector2 scrollPos = Vector2.zero;
		using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(100), GUILayout.Width(300)))
		{
			scrollPos = scrollView.scrollPosition;
			GUILayout.Label("This is a string inside a Scroll view! This is a string inside a Scroll view! This is a string inside a Scroll view!This is a string inside a Scroll view! This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!\n This is a string inside a Scroll view!");
		}

		// AreaScope
		Rect zone = new Rect(0, 200, 100, 100);
		//Rect zone = EditorGUILayout.GetControlRect();
		EditorGUI.DrawRect(zone, new Color(1, 0, 0, 0.5f));
		using (var scrollView = new GUILayout.AreaScope(zone))
		{
			GUILayout.Label("This is a string inside a Scroll view! ");
		}

		#endregion

		#region EditorGUILayout
		//Vertical
		using (new EditorGUILayout.VerticalScope()) { }
		//Horizontal
		using (new EditorGUILayout.HorizontalScope()) { }
		//Toggle
		bool GroupEnabled = true;
		bool[] bools = new bool[3] { true, true, true };
		using (var posGroup = new EditorGUILayout.ToggleGroupScope("Toggle Label", GroupEnabled))
		{
			GroupEnabled = posGroup.enabled;
			bools[0] = EditorGUILayout.Toggle("a", bools[0]);
			bools[1] = EditorGUILayout.Toggle("b", bools[1]);
			bools[2] = EditorGUILayout.Toggle("c", bools[2]);
		}
		#endregion

		#endregion

		//Space
		GUILayout.Space(2);
		GUILayout.Space(EditorGUIUtility.singleLineHeight);

		#region Shlag

		// Les fields
		///https://docs.unity3d.com/ScriptReference/EditorGUILayout.html
		//Object Tricks
		///script.someRef = EditorGUILayout.ObjectField("someTransform", script.someRef, typeof(Transform), true) as Transform;

		//Save
		EditorUtility.SetDirty(target);
		#endregion

		serializedObject.ApplyModifiedProperties();
	}
}
#endif