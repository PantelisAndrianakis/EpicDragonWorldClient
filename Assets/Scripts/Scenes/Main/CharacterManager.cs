﻿using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

/**
 * Author: Pantelis Andrianakis
 * Date: December 27th 2018
 */
public class CharacterManager : MonoBehaviour
{
    public static CharacterManager Instance { get; private set; }

    public DynamicCharacterAvatar avatarMale;
    public DynamicCharacterAvatar avatarFemale;

    public List<string> hairModelsMale = new List<string>();
    public List<string> hairModelsFemale = new List<string>();

    public ConcurrentDictionary<long, CharacterDataHolder> characterCreationQueue = new ConcurrentDictionary<long, CharacterDataHolder>();

    private void Start()
    {
        Instance = this;

        StartCoroutine(CreationQueueCoroutine());
    }

    private IEnumerator CreationQueueCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            lock (WorldManager.updateObjectLock)
            {
                foreach (KeyValuePair<long, CharacterDataHolder> entry in characterCreationQueue)
                {
                    // Object does not exist. Instantiate.
                    GameObject newObj = CreateCharacter(entry.Value).gameObject;

                    // Assign object id and name.
                    WorldObject worldObject = newObj.AddComponent<WorldObject>();
                    worldObject.objectId = entry.Key;
                    worldObject.characterData = entry.Value;
                    WorldObjectText worldObjectText = newObj.AddComponent<WorldObjectText>();
                    worldObjectText.worldObjectName = entry.Value.GetName();
                    worldObjectText.attachedObject = newObj;
                    worldObjectText.worldObject = worldObject;

                    ((IDictionary<long, GameObject>)WorldManager.Instance.gameObjects).Remove(entry.Key);
                    WorldManager.Instance.gameObjects.TryAdd(entry.Key, newObj);

                    ((IDictionary<long, CharacterDataHolder>)characterCreationQueue).Remove(entry.Key);
                }
            }
        }
    }

    public DynamicCharacterAvatar CreateCharacter(CharacterDataHolder characterData)
    {
        return CreateCharacter(characterData, characterData.GetX(), characterData.GetY(), characterData.GetZ(), characterData.GetHeading());
    }

    public DynamicCharacterAvatar CreateCharacter(CharacterDataHolder characterData, float posX, float posY, float posZ, float heading)
    {
        DynamicCharacterAvatar newAvatar = CreateCharacter(characterData, posX, posY, posZ);

        // Rotation.
        Quaternion curHeading = newAvatar.gameObject.transform.localRotation;
        Vector3 curvAngle = curHeading.eulerAngles;
        curvAngle.y = heading;
        curHeading.eulerAngles = curvAngle;
        newAvatar.gameObject.transform.localRotation = curHeading;

        // Return the new DynamicCharacterAvatar.
        return newAvatar;
    }

    public DynamicCharacterAvatar CreateCharacter(CharacterDataHolder characterData, float posX, float posY, float posZ)
    {
        // Setting race on Instantiate, because even we set it at CustomizeCharacterAppearance, we could not mount items for female characters.
        Vector3 newPosition = new Vector3(posX, posY, posZ);
        DynamicCharacterAvatar avatarTemplate = characterData.GetRace() == 0 ? avatarMale : avatarFemale;
        DynamicCharacterAvatar newAvatar = Instantiate(avatarTemplate, newPosition, Quaternion.identity) as DynamicCharacterAvatar;

        // Prevent UMA bone error.
        newAvatar.BuildCharacter();

        // Add a new Capsule Collider to prevent falling.
        CapsuleCollider capsuleCollider = newAvatar.gameObject.AddComponent<CapsuleCollider>();
        capsuleCollider.radius = 0.15f;
        capsuleCollider.height = 1.71f;
        capsuleCollider.center = new Vector3(0, 0.82f, 0);

        // Add AudioSource.
        newAvatar.gameObject.AddComponent<AudioSource>();

        // Disable avatar until race customization ends.
        newAvatar.gameObject.SetActive(false);

        // Customize character.
        StartCoroutine(CustomizeCharacterAppearance(characterData, newAvatar));

        // Return GameObject.
        return newAvatar;
    }

    public IEnumerator CustomizeCharacterAppearance(CharacterDataHolder characterData, DynamicCharacterAvatar newAvatar)
    {
        // Unfortunately UMA needs a small delay to initialize.
        // Without this delay, on slower machines, we got a crash.
        yield return new WaitForSeconds(0.25f);

        if (newAvatar != null)
        {
            // Re-enable avatar since customization delay ended.
            newAvatar.gameObject.SetActive(true);

            // Customize character.
            int hairType = characterData.GetHairType();
            if (characterData.GetRace() == 0)
            {
                newAvatar.ChangeRace("HumanMaleDCS");
                if (hairType != 0)
                {
                    newAvatar.SetSlot("Hair", hairModelsMale[characterData.GetHairType()]);
                }
            }
            if (characterData.GetRace() == 1)
            {
                newAvatar.ChangeRace("HumanFemaleDCS");
                if (hairType != 0)
                {
                    newAvatar.SetSlot("Hair", hairModelsFemale[characterData.GetHairType()]);
                }
            }

            // Set colors.
            newAvatar.SetColor("Hair", Util.IntToColor(characterData.GetHairColor()));
            newAvatar.SetColor("Skin", Util.IntToColor(characterData.GetSkinColor()));
            newAvatar.SetColor("Eyes", Util.IntToColor(characterData.GetEyeColor()));
            newAvatar.UpdateColors(true);

            Dictionary<string, DnaSetter> dna = newAvatar.GetDNA();
            dna["height"].Set(characterData.GetHeight());
            dna["belly"].Set(characterData.GetBelly());
            newAvatar.BuildCharacter();

            // Set visible equipable armor items.
            EquipItem(newAvatar, characterData.GetHeadItem());
            EquipItem(newAvatar, characterData.GetChestItem());
            EquipItem(newAvatar, characterData.GetLegsItem());
            EquipItem(newAvatar, characterData.GetHandsItem());
            EquipItem(newAvatar, characterData.GetFeetItem());

            // Without this delay, sometimes, we cannot not see mounted weapons.
            yield return new WaitForSeconds(0.25f);

            // Set visible equipable left and right hand items.
            EquipItem(newAvatar, characterData.GetLeftHandItem());
            EquipItem(newAvatar, characterData.GetRightHandItem());
        }
    }

    public void EquipItem(DynamicCharacterAvatar avatar, int id)
    {
        ItemHolder item = ItemData.GetItem(id);
        if (item == null || item.GetItemType() != ItemType.EQUIP)
        {
            return;
        }

        // UMAData must not be null, so wait until it is not.
        UMAData umaData = null;
        while (umaData == null)
        {
            umaData = avatar.gameObject.GetComponent<UMAData>();
        }

        bool isMale = avatar.activeRace.name.Equals("HumanMaleDCS");
        switch (item.GetItemSlot())
        {
            case ItemSlot.HEAD:
                avatar.SetSlot("Head", isMale ? item.GetRecipeMale() : item.GetRecipeFemale());
                avatar.BuildCharacter();
                break;

            case ItemSlot.CHEST:
                avatar.SetSlot("Chest", isMale ? item.GetRecipeMale() : item.GetRecipeFemale());
                avatar.BuildCharacter();
                break;

            case ItemSlot.LEGS:
                avatar.SetSlot("Legs", isMale ? item.GetRecipeMale() : item.GetRecipeFemale());
                avatar.BuildCharacter();
                break;

            case ItemSlot.HANDS:
                avatar.SetSlot("Hands", isMale ? item.GetRecipeMale() : item.GetRecipeFemale());
                avatar.BuildCharacter();
                break;

            case ItemSlot.FEET:
                avatar.SetSlot("Feet", isMale ? item.GetRecipeMale() : item.GetRecipeFemale());
                avatar.BuildCharacter();
                break;

            case ItemSlot.LEFT_HAND:
                UnEquipItem(avatar, ItemSlot.LEFT_HAND);
                // Find left hand bone.
                GameObject boneObjL = umaData.GetBoneGameObject("LeftHand");
                // Create the item.
                GameObject newObjL = Instantiate(ItemData.Instance.itemPrefabs[item.GetPrefabId()]);
                newObjL.name = "LeftHandItem";
                newObjL.transform.SetParent(boneObjL.transform, false);
                if (isMale)
                {
                    newObjL.transform.localPosition = item.GetPositionMale();
                    newObjL.transform.localRotation = item.GetRotationMale();
                    newObjL.transform.localScale = item.GetScaleMale();
                }
                else
                {
                    newObjL.transform.localPosition = item.GetPositionFemale();
                    newObjL.transform.localRotation = item.GetRotationFemale();
                    newObjL.transform.localScale = item.GetScaleFemale();
                }
                break;

            case ItemSlot.RIGHT_HAND:
                UnEquipItem(avatar, ItemSlot.RIGHT_HAND);
                // Find right hand bone.
                GameObject boneObjR = umaData.GetBoneGameObject("RightHand");
                // Create the item.
                GameObject newObjR = Instantiate(ItemData.Instance.itemPrefabs[item.GetPrefabId()]);
                newObjR.name = "RightHandItem";
                newObjR.transform.SetParent(boneObjR.transform, false);
                if (isMale)
                {
                    newObjR.transform.localPosition = item.GetPositionMale();
                    newObjR.transform.localRotation = item.GetRotationMale();
                    newObjR.transform.localScale = item.GetScaleMale();
                }
                else
                {
                    newObjR.transform.localPosition = item.GetPositionFemale();
                    newObjR.transform.localRotation = item.GetRotationFemale();
                    newObjR.transform.localScale = item.GetScaleFemale();
                }
                break;

            case ItemSlot.TWO_HAND:
                UnEquipItem(avatar, ItemSlot.TWO_HAND);
                // Find right hand bone.
                GameObject boneObjTH = umaData.GetBoneGameObject("RightHand");
                // Create the item.
                GameObject newObjTH = Instantiate(ItemData.Instance.itemPrefabs[item.GetPrefabId()]);
                newObjTH.name = "TwoHandItem";
                boneObjTH.transform.SetParent(boneObjTH.transform, false);
                if (isMale)
                {
                    boneObjTH.transform.localPosition = item.GetPositionMale();
                    boneObjTH.transform.localRotation = item.GetRotationMale();
                    boneObjTH.transform.localScale = item.GetScaleMale();
                }
                else
                {
                    boneObjTH.transform.localPosition = item.GetPositionFemale();
                    boneObjTH.transform.localRotation = item.GetRotationFemale();
                    boneObjTH.transform.localScale = item.GetScaleFemale();
                }
                break;
        }
    }

    public void UnEquipItem(DynamicCharacterAvatar avatar, ItemSlot itemSlot)
    {
        // UMAData must not be null, so wait until it is not.
        UMAData umaData = null;
        while (umaData == null)
        {
            umaData = avatar.gameObject.GetComponent<UMAData>();
        }

        switch (itemSlot)
        {
            case ItemSlot.HEAD:
                avatar.ClearSlot("Head");
                avatar.BuildCharacter();
                break;

            case ItemSlot.CHEST:
                avatar.ClearSlot("Chest");
                avatar.BuildCharacter();
                break;

            case ItemSlot.LEGS:
                avatar.ClearSlot("Legs");
                avatar.BuildCharacter();
                break;

            case ItemSlot.HANDS:
                avatar.ClearSlot("Hands");
                avatar.BuildCharacter();
                break;

            case ItemSlot.FEET:
                avatar.ClearSlot("Feet");
                avatar.BuildCharacter();
                break;

            case ItemSlot.LEFT_HAND:
                // Find left hand bone.
                GameObject boneObjL = umaData.GetBoneGameObject("LeftHand");
                // If previous item exists remove it.
                Transform objTransformL = boneObjL.transform.Find("LeftHandItem");
                if (objTransformL != null)
                {
                    Destroy(objTransformL.gameObject);
                }
                // Find right hand bone.
                GameObject boneObjLR = umaData.GetBoneGameObject("RightHand");
                // If previous two hand item exists remove it.
                Transform objTransformL2 = boneObjLR.transform.Find("TwoHandItem");
                if (objTransformL2 != null)
                {
                    Destroy(objTransformL2.gameObject);
                }
                break;

            case ItemSlot.RIGHT_HAND:
                // Find right hand bone.
                GameObject boneObjR = umaData.GetBoneGameObject("RightHand");
                // If previous item exists remove it.
                Transform objTransformR = boneObjR.transform.Find("RightHandItem");
                if (objTransformR != null)
                {
                    Destroy(objTransformR.gameObject);
                }
                // If previous two hand item exists remove it.
                Transform objTransformR2 = boneObjR.transform.Find("TwoHandItem");
                if (objTransformR2 != null)
                {
                    Destroy(objTransformR2.gameObject);
                }
                break;

            case ItemSlot.TWO_HAND:
                // Find left hand bone.
                GameObject boneObjTHL = umaData.GetBoneGameObject("LeftHand");
                // If previous left hand item exists remove it.
                Transform objTransformTH1 = boneObjTHL.transform.Find("LeftHandItem");
                if (objTransformTH1 != null)
                {
                    Destroy(objTransformTH1.gameObject);
                }
                // Find right hand bone.
                GameObject boneObjTHR = umaData.GetBoneGameObject("RightHand");
                // If previous right hand item exists remove it.
                Transform objTransformTH2 = boneObjTHR.transform.Find("RightHandItem");
                if (objTransformTH2 != null)
                {
                    Destroy(objTransformTH2.gameObject);
                }
                // If previous two hand item exists remove it.
                Transform objTransformTH3 = boneObjTHR.transform.Find("TwoHandItem");
                if (objTransformTH3 != null)
                {
                    Destroy(objTransformTH3.gameObject);
                }
                break;
        }
    }
}
