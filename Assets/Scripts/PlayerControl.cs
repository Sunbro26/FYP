using UnityEngine;
using TMPro;
using UnityEngine.UI;
public class PlayerControl : MonoBehaviour
{
    public Slider healthbar;
    public TMP_Text healthText;
    public int health = 100;
    public int maxHealth = 0;
    void Start()
    {
        maxHealth = health;
    }

    void Update()
    {
        healthText.text = health + " / " + maxHealth;
        healthbar.value = (float)health / (float)maxHealth;
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.tag== "sword")
        {
            health = health - 10;
        }
    }
}
