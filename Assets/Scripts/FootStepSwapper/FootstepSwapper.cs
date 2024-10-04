using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FootstepSwapper : MonoBehaviour
{
    TerrainChecker checker;
    CPMPlayer fpc;
    string currentLayer;
    public FootstepCollection[] terrainFootstepCollections;
    //Checker es Terrain Cheker

    // Start is called before the first frame update
    private void Awake()
    {
        fpc = GetComponent<CPMPlayer>();
        checker = new TerrainChecker();
    }
 
    
    public void CheckLayers()
    {
        //raycast down
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 3))
        {
            
            //check if terrain exists
            if (hit.transform.GetComponent<Terrain>() != null)
            {
                Terrain t = hit.transform.GetComponent<Terrain>();
                //if layer matches our currentLayer
                if (currentLayer != checker.GetLayerName(transform.position, t))
                {
                    currentLayer = checker.GetLayerName(transform.position, t); //swap footsteps!
                    foreach (FootstepCollection collection in terrainFootstepCollections)
                    {
                        if (currentLayer == collection.name)
                        {
                            fpc.SwapFootSteps(collection);
                        }
                    }
                }
               
            }

            if (hit.transform.GetComponent<SurfaceType>() != null)
            {
                FootstepCollection collection = hit.transform.GetComponent<SurfaceType>().footstepCollection;
                currentLayer = collection.name;
                fpc.SwapFootSteps(collection);
            }

        }
    }
    
}