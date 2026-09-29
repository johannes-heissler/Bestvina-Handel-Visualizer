# Visualizing the Bestvina-Handel algorithm

Homeomorphisms of a punctured genus $g$ surface can be studied by studying the induced map on an embedded graph.
The Bestvina-Handel algorithm is a procedure to modify this graph and the corresponding map on it until it can be turned into a preserved train track.
This allows one to study the mapping class group combinatorially while at the same time drawing pretty pictures and makes the mapping class group more hands-on.
The previous algorithms didn't really allow you to see exactly what is happening or give you the choice that is left in this "algorithm".
This program gives all of that to you.

## New version
The following repo makes this obsolete: [https://github.com/johannes-heissler/bestvina-handel-web]

## Screenshots
Just enter the map as it acts on the graph and you can click through all steps, even returning to see what a different choice might have given you.
![grafik](https://github.com/user-attachments/assets/ff2813d7-209f-4dd7-be81-92a9568e2d52)



The original goal of this program was to also include embeddings of the surfaces and that is all also implemented. 
I just need actual maps from hyperbolic (ideal) polygons into R^3 and those are harder to do.
This is how it could look like - here with a non-punctured torus, which is flat.
![grafik](https://github.com/user-attachments/assets/ca6b7784-56de-4e9b-90a8-ad3faceb4cda)

There is still much bug fixing and polishing to do, in particular regarding the embedding. 
This will not happen anymore, see the new repo above.
